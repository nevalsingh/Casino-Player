/*
    OT Assessment DB - schema, TVP, and stored procedures.

    Design summary (see Notes.md for the full rationale):
    - Provider / Game / Player are normalised out of the wager payload: the tester reuses 100 providers x 10
      games and 1000 players across 7000 wagers, so storing the strings on every row would be pure duplication.
    - CasinoWager uses a surrogate BIGINT IDENTITY clustered key so inserts are always append-only (no page
      splits from a random GUID clustered key), with WagerId as a separate unique index used for idempotency.
    - SessionData from the source payload is intentionally not stored - nothing in the required endpoints reads
      it, and it is by far the largest field per row.
    - Player.TotalAmountSpend is a running total maintained by the insert proc (from OUTPUT INSERTED, so
      redelivered/duplicate wagers never double-count), so the leaderboard query is a plain indexed TOP(n)
      instead of aggregating all wagers on every call.
    - READ_COMMITTED_SNAPSHOT is turned on so the GET endpoints read committed data without blocking behind the
      consumer's batch insert transactions during the load test.

    This script is idempotent: it can be run repeatedly against the same server without error.
*/

IF DB_ID('OT_Assessment_DB') IS NULL
BEGIN
    CREATE DATABASE OT_Assessment_DB;
END
GO

ALTER DATABASE OT_Assessment_DB SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO

USE OT_Assessment_DB;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- ============================================================================
-- Tables
-- ============================================================================

IF OBJECT_ID('dbo.Provider', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Provider
    (
        ProviderId INT IDENTITY (1, 1) NOT NULL,
        Name       NVARCHAR(200)       NOT NULL,
        CONSTRAINT PK_Provider PRIMARY KEY CLUSTERED (ProviderId),
        CONSTRAINT UQ_Provider_Name UNIQUE (Name)
    );
END
GO

IF OBJECT_ID('dbo.Game', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game
    (
        GameId     INT IDENTITY (1, 1) NOT NULL,
        ProviderId INT                 NOT NULL,
        Name       NVARCHAR(200)       NOT NULL,
        Theme      NVARCHAR(50)        NULL,
        CONSTRAINT PK_Game PRIMARY KEY CLUSTERED (GameId),
        CONSTRAINT FK_Game_Provider FOREIGN KEY (ProviderId) REFERENCES dbo.Provider (ProviderId),
        CONSTRAINT UQ_Game_ProviderId_Name UNIQUE (ProviderId, Name)
    );
END
GO

IF OBJECT_ID('dbo.Player', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Player
    (
        AccountId        UNIQUEIDENTIFIER NOT NULL,
        Username         NVARCHAR(100)    NOT NULL,
        TotalAmountSpend DECIMAL(19, 4)   NOT NULL CONSTRAINT DF_Player_TotalAmountSpend DEFAULT (0),
        CONSTRAINT PK_Player PRIMARY KEY CLUSTERED (AccountId)
    );

    CREATE NONCLUSTERED INDEX IX_Player_TotalAmountSpend
        ON dbo.Player (TotalAmountSpend DESC) INCLUDE (Username);
END
GO

IF OBJECT_ID('dbo.CasinoWager', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CasinoWager
    (
        CasinoWagerId       BIGINT IDENTITY (1, 1) NOT NULL,
        WagerId              UNIQUEIDENTIFIER       NOT NULL,
        AccountId            UNIQUEIDENTIFIER       NOT NULL,
        GameId               INT                    NOT NULL,
        TransactionId        UNIQUEIDENTIFIER       NOT NULL,
        BrandId              UNIQUEIDENTIFIER       NOT NULL,
        ExternalReferenceId  UNIQUEIDENTIFIER       NOT NULL,
        TransactionTypeId    UNIQUEIDENTIFIER       NOT NULL,
        Amount               DECIMAL(19, 4)         NOT NULL,
        CreatedDateTimeUtc   DATETIME2(7)           NOT NULL,
        NumberOfBets         INT                    NOT NULL,
        CountryCode          CHAR(2)                NOT NULL,
        DurationMs           BIGINT                 NOT NULL,
        IngestedDateTimeUtc  DATETIME2(7)           NOT NULL CONSTRAINT DF_CasinoWager_IngestedDateTimeUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_CasinoWager PRIMARY KEY CLUSTERED (CasinoWagerId),
        CONSTRAINT FK_CasinoWager_Player FOREIGN KEY (AccountId) REFERENCES dbo.Player (AccountId),
        CONSTRAINT FK_CasinoWager_Game FOREIGN KEY (GameId) REFERENCES dbo.Game (GameId)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_CasinoWager_WagerId ON dbo.CasinoWager (WagerId);

    -- Serves the paged history query: seek on AccountId, ordered range on CreatedDateTimeUtc, no key lookup.
    CREATE NONCLUSTERED INDEX IX_CasinoWager_AccountId_CreatedDateTimeUtc
        ON dbo.CasinoWager (AccountId, CreatedDateTimeUtc DESC) INCLUDE (WagerId, GameId, Amount);
END
GO

-- ============================================================================
-- Table type for batch inserts
-- ============================================================================

IF TYPE_ID('dbo.CasinoWagerTableType') IS NULL
BEGIN
    CREATE TYPE dbo.CasinoWagerTableType AS TABLE
    (
        WagerId             UNIQUEIDENTIFIER NOT NULL,
        ProviderName         NVARCHAR(200)    NOT NULL,
        GameName             NVARCHAR(200)    NOT NULL,
        Theme                NVARCHAR(50)     NULL,
        AccountId            UNIQUEIDENTIFIER NOT NULL,
        Username             NVARCHAR(100)    NOT NULL,
        TransactionId        UNIQUEIDENTIFIER NOT NULL,
        BrandId              UNIQUEIDENTIFIER NOT NULL,
        ExternalReferenceId  UNIQUEIDENTIFIER NOT NULL,
        TransactionTypeId    UNIQUEIDENTIFIER NOT NULL,
        Amount               DECIMAL(19, 4)   NOT NULL,
        CreatedDateTimeUtc   DATETIME2(7)     NOT NULL,
        NumberOfBets         INT              NOT NULL,
        CountryCode          CHAR(2)          NOT NULL,
        DurationMs           BIGINT           NOT NULL
        -- Deliberately no PRIMARY KEY on WagerId here: a redelivered message can appear twice within one
        -- batch, so duplicates are removed inside the proc instead of failing the whole batch.
    );
END
GO

-- ============================================================================
-- dbo.usp_CasinoWager_InsertBatch
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_CasinoWager_InsertBatch
    @Wagers dbo.CasinoWagerTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- De-duplicate within the batch itself (at-least-once delivery can redeliver a WagerId in one flush).
    ;WITH DedupedWagers AS
    (
        SELECT *,
               ROW_NUMBER() OVER (PARTITION BY WagerId ORDER BY CreatedDateTimeUtc DESC) AS RowNum
        FROM @Wagers
    )
    SELECT WagerId, ProviderName, GameName, Theme, AccountId, Username, TransactionId, BrandId,
           ExternalReferenceId, TransactionTypeId, Amount, CreatedDateTimeUtc, NumberOfBets, CountryCode, DurationMs
    INTO #Wagers
    FROM DedupedWagers
    WHERE RowNum = 1;

    -- One row per AccountId, using the username from the most recent wager in this batch.
    SELECT AccountId, Username
    INTO #LatestUsername
    FROM (
        SELECT AccountId, Username,
               ROW_NUMBER() OVER (PARTITION BY AccountId ORDER BY CreatedDateTimeUtc DESC) AS RowNum
        FROM #Wagers
    ) x
    WHERE RowNum = 1;

    BEGIN TRANSACTION;

    BEGIN TRY
        -- 1. Providers. UPDLOCK+HOLDLOCK on the target table (not the #temp table) so a concurrent consumer
        -- checking the same Name is blocked until this transaction commits, instead of both seeing "not
        -- exists" and both inserting - which would raise a UQ_Provider_Name violation.
        INSERT INTO dbo.Provider (Name)
        SELECT DISTINCT w.ProviderName
        FROM #Wagers w
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Provider p WITH (UPDLOCK, HOLDLOCK) WHERE p.Name = w.ProviderName);

        -- 2. Games. Built after Providers are inserted above, so brand-new providers in this same batch
        -- already have a ProviderId to join to. One row per Game key even if Theme differs across wagers
        -- in this batch (last-write-wins via MAX). Same locking rationale as Providers.
        SELECT p2.ProviderId, w2.GameName AS Name, MAX(w2.Theme) AS Theme
        INTO #Games
        FROM #Wagers w2
                 JOIN dbo.Provider p2 ON p2.Name = w2.ProviderName
        GROUP BY p2.ProviderId, w2.GameName;

        INSERT INTO dbo.Game (ProviderId, Name, Theme)
        SELECT src.ProviderId, src.Name, src.Theme
        FROM #Games src
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.Game g WITH (UPDLOCK, HOLDLOCK)
            WHERE g.ProviderId = src.ProviderId AND g.Name = src.Name);

        -- 3. Players: insert new ones, refresh username to the latest value seen in this batch.
        INSERT INTO dbo.Player (AccountId, Username, TotalAmountSpend)
        SELECT lu.AccountId, lu.Username, 0
        FROM #LatestUsername lu
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Player pl WITH (UPDLOCK, HOLDLOCK) WHERE pl.AccountId = lu.AccountId);

        UPDATE pl
        SET pl.Username = lu.Username
        FROM dbo.Player pl
                 JOIN #LatestUsername lu ON lu.AccountId = pl.AccountId
        WHERE pl.Username <> lu.Username;

        -- 4. Wagers: skip WagerIds already stored (idempotent under at-least-once redelivery). Provider is
        -- joined explicitly (not a correlated scalar subquery) to reach Game via its ProviderId.
        DECLARE @Inserted TABLE (AccountId UNIQUEIDENTIFIER NOT NULL, Amount DECIMAL(19, 4) NOT NULL);

        INSERT INTO dbo.CasinoWager
            (WagerId, AccountId, GameId, TransactionId, BrandId, ExternalReferenceId, TransactionTypeId,
             Amount, CreatedDateTimeUtc, NumberOfBets, CountryCode, DurationMs)
        OUTPUT INSERTED.AccountId, INSERTED.Amount INTO @Inserted (AccountId, Amount)
        SELECT w.WagerId, w.AccountId, g.GameId, w.TransactionId, w.BrandId, w.ExternalReferenceId,
               w.TransactionTypeId, w.Amount, w.CreatedDateTimeUtc, w.NumberOfBets, w.CountryCode, w.DurationMs
        FROM #Wagers w
                 JOIN dbo.Provider p ON p.Name = w.ProviderName
                 JOIN dbo.Game g ON g.ProviderId = p.ProviderId AND g.Name = w.GameName
        WHERE NOT EXISTS (SELECT 1 FROM dbo.CasinoWager cw WITH (UPDLOCK, HOLDLOCK) WHERE cw.WagerId = w.WagerId);

        -- 5. Running total: only rows actually inserted contribute, so duplicates never double-count.
        UPDATE pl
        SET pl.TotalAmountSpend = pl.TotalAmountSpend + s.Total
        FROM dbo.Player pl
                 JOIN (SELECT AccountId, SUM(Amount) AS Total FROM @Inserted GROUP BY AccountId) s
                      ON s.AccountId = pl.AccountId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT COUNT(*) FROM @Inserted;
END
GO

-- ============================================================================
-- dbo.usp_CasinoWager_GetPageByAccountId
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_CasinoWager_GetPageByAccountId
    @AccountId UNIQUEIDENTIFIER,
    @PageNumber INT = 1,
    @PageSize INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(*)
    FROM dbo.CasinoWager
    WHERE AccountId = @AccountId;

    SELECT cw.WagerId,
           g.Name     AS Game,
           p.Name     AS Provider,
           cw.Amount,
           cw.CreatedDateTimeUtc AS CreatedDate
    FROM dbo.CasinoWager cw
             JOIN dbo.Game g ON g.GameId = cw.GameId
             JOIN dbo.Provider p ON p.ProviderId = g.ProviderId
    WHERE cw.AccountId = @AccountId
    ORDER BY cw.CreatedDateTimeUtc DESC, cw.CasinoWagerId DESC
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

-- ============================================================================
-- dbo.usp_Player_GetTopSpenders
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_Player_GetTopSpenders
    @Count INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Count) AccountId, Username, TotalAmountSpend
    FROM dbo.Player
    ORDER BY TotalAmountSpend DESC, AccountId;
END
GO
