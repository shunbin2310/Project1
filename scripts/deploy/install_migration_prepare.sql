-- ADMINISTRATOR INSTALLATION ONLY. NEVER invoked by CI or the preparation helper.
-- Secrets are random hex placeholders replaced in a private installer file, not sqlcmd variables.
USE [master];
SET NOCOUNT ON;
IF CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) <> N'homelab-server'
    OR DB_ID(N'Project1Db') IS NULL OR IS_SRVROLEMEMBER('sysadmin') <> 1
    THROW 51000, 'Unexpected installation target or administrator.', 1;
IF SUSER_ID(N'project1_backup') IS NOT NULL
    OR CERT_ID(N'Project1PrepareBackupCert') IS NOT NULL
    OR OBJECT_ID(N'dbo.project1_prepare_backup') IS NOT NULL
    THROW 51000, 'Existing backup installation requires administrator review; do not overwrite.', 1;
IF EXISTS (SELECT 1 FROM [Project1Db].sys.certificates WHERE name = N'Project1PrepareBackupCert')
    OR EXISTS (SELECT 1 FROM [Project1Db].sys.database_principals WHERE name = N'Project1PrepareBackupCertUser')
    OR SUSER_ID(N'Project1PrepareBackupCertLogin') IS NOT NULL
    OR USER_ID(N'project1_backup') IS NOT NULL
    THROW 51000, 'Existing certificate/user requires administrator review.', 1;
CREATE LOGIN [project1_backup] WITH PASSWORD = N'__BACKUP_PASSWORD__', CHECK_POLICY = ON,
    CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [master];
CREATE USER [project1_backup] FOR LOGIN [project1_backup];
CREATE CERTIFICATE [Project1PrepareBackupCert]
    ENCRYPTION BY PASSWORD = '__SIGNING_PASSWORD__'
    -- Keep the subject independent of database names and within Linux's 64-character limit.
    WITH SUBJECT = 'Fixed backup and VERIFYONLY procedure';
CREATE LOGIN [Project1PrepareBackupCertLogin] FROM CERTIFICATE [Project1PrepareBackupCert];
-- This non-authenticating certificate identity supplies VERIFYONLY permission only inside the signed module.
GRANT CREATE ANY DATABASE TO [Project1PrepareBackupCertLogin];
DECLARE @Certificate nvarchar(max) = CONVERT(nvarchar(max), CERTENCODED(CERT_ID(N'Project1PrepareBackupCert')), 1);
EXEC (N'USE [Project1Db]; CREATE CERTIFICATE [Project1PrepareBackupCert] FROM BINARY = ' + @Certificate + N';
CREATE USER [Project1PrepareBackupCertUser] FROM CERTIFICATE [Project1PrepareBackupCert];
GRANT BACKUP DATABASE TO [Project1PrepareBackupCertUser];');
GO
CREATE PROCEDURE dbo.project1_prepare_backup @Token nvarchar(128)
AS
BEGIN
    SET NOCOUNT ON;
    IF ORIGINAL_LOGIN() <> N'project1_backup'
        OR CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) <> N'homelab-server'
        OR DB_NAME() <> N'master'
        THROW 51000, 'Unexpected fixed backup identity.', 1;
    IF @Token IS NULL OR LEN(@Token) <> 32 OR DATALENGTH(@Token) <> 64
        OR @Token COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9a-f]%'
        THROW 51000, 'Invalid backup token.', 1;
    IF ISNULL(CONVERT(nvarchar(60), DATABASEPROPERTYEX(N'Project1Db', 'Status')), N'UNKNOWN') <> N'ONLINE'
        THROW 51000, 'Target database is not online.', 1;
    DECLARE @File nvarchar(260) = N'/var/opt/mssql/backup/project1-prepare-' + @Token + N'/Project1Db.bak';
    BEGIN TRY
        BACKUP DATABASE [Project1Db] TO DISK = @File
            WITH COPY_ONLY, CHECKSUM, STOP_ON_ERROR;
        RESTORE VERIFYONLY FROM DISK = @File WITH FILE = 1, CHECKSUM, STOP_ON_ERROR;
        SELECT N'PROJECT1_PREPARE_BACKUP_V1';
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH;
END;
GO
ADD SIGNATURE TO dbo.project1_prepare_backup BY CERTIFICATE [Project1PrepareBackupCert]
    WITH PASSWORD = '__SIGNING_PASSWORD__';
GRANT EXECUTE ON dbo.project1_prepare_backup TO [project1_backup];
-- No private signing key remains available for extending this module's permissions.
ALTER CERTIFICATE [Project1PrepareBackupCert] REMOVE PRIVATE KEY;
SELECT N'PROJECT1_PREPARE_INSTALL_V1';
