-- ONE-TIME ADMINISTRATOR REVIEW ONLY. Never executed by CI or the deployment script.
-- First create the dedicated SQL login project1_app privately; never paste its password.
-- This grants access to the EXISTING Project1Db. No database/table/data is recreated.
USE [Project1Db];
GO
SET XACT_ABORT ON;
IF LOWER(CONVERT(nvarchar(128), SERVERPROPERTY('MachineName'))) <> N'homelab-server'
    THROW 50000, 'Wrong SQL Server.', 1;
IF SUSER_ID(N'project1_app') IS NULL
    THROW 50000, 'Create the dedicated SQL login privately first.', 1;
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin', N'project1_app'), 1) <> 0
    THROW 50000, 'Application login must not be sysadmin.', 1;
BEGIN TRANSACTION;
IF USER_ID(N'project1_app') IS NULL
    CREATE USER [project1_app] FOR LOGIN [project1_app];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals
               WHERE name=N'project1_app' AND sid=SUSER_SID(N'project1_app'))
    THROW 50000, 'Unexpected database user mapping.', 1;
IF COALESCE(IS_ROLEMEMBER(N'db_owner', N'project1_app'), 1) <> 0
    THROW 50000, 'Application user must not be db_owner.', 1;
IF IS_ROLEMEMBER(N'db_datareader', N'project1_app') <> 1
    ALTER ROLE [db_datareader] ADD MEMBER [project1_app];
IF IS_ROLEMEMBER(N'db_datawriter', N'project1_app') <> 1
    ALTER ROLE [db_datawriter] ADD MEMBER [project1_app];
-- Startup migration needs broad database-scoped DDL; this is not AddColumn-only.
IF IS_ROLEMEMBER(N'db_ddladmin', N'project1_app') <> 1
    ALTER ROLE [db_ddladmin] ADD MEMBER [project1_app];
COMMIT;
GO
