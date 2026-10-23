using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project1.Api.Migrations
{
    /// <inheritdoc />
    public partial class RetireCatalogManagerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Roles may not exist yet on a fresh database, before IdentitySeeder runs.
                INSERT INTO [AspNetRoles] ([Name], [NormalizedName], [ConcurrencyStamp])
                SELECT 'PROCUREMENT_OFFICER', 'PROCUREMENT_OFFICER', CAST(NEWID() AS nvarchar(36))
                WHERE NOT EXISTS (
                    SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = 'PROCUREMENT_OFFICER');

                -- Invalidate only affected sessions, including users who already hold both roles.
                UPDATE [AspNetUsers]
                SET [SecurityStamp] = CAST(NEWID() AS nvarchar(36)),
                    [ConcurrencyStamp] = CAST(NEWID() AS nvarchar(36))
                WHERE EXISTS (
                    SELECT 1 FROM [AspNetUserRoles] AS membership
                    INNER JOIN [AspNetRoles] AS role ON role.[Id] = membership.[RoleId]
                    WHERE membership.[UserId] = [AspNetUsers].[Id]
                      AND role.[NormalizedName] = 'CATALOG_MANAGER');

                INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
                SELECT legacy.[UserId], procurement.[Id]
                FROM [AspNetUserRoles] AS legacy
                INNER JOIN [AspNetRoles] AS retired ON retired.[Id] = legacy.[RoleId]
                CROSS JOIN [AspNetRoles] AS procurement
                WHERE retired.[NormalizedName] = 'CATALOG_MANAGER'
                  AND procurement.[NormalizedName] = 'PROCUREMENT_OFFICER'
                  AND NOT EXISTS (
                    SELECT 1 FROM [AspNetUserRoles] AS existing
                    WHERE existing.[UserId] = legacy.[UserId] AND existing.[RoleId] = procurement.[Id]);

                -- Retain any custom role claims without duplicating claims already on Procurement.
                INSERT INTO [AspNetRoleClaims] ([RoleId], [ClaimType], [ClaimValue])
                SELECT DISTINCT procurement.[Id], claim.[ClaimType], claim.[ClaimValue]
                FROM [AspNetRoleClaims] AS claim
                INNER JOIN [AspNetRoles] AS retired ON retired.[Id] = claim.[RoleId]
                CROSS JOIN [AspNetRoles] AS procurement
                WHERE retired.[NormalizedName] = 'CATALOG_MANAGER'
                  AND procurement.[NormalizedName] = 'PROCUREMENT_OFFICER'
                  AND NOT EXISTS (
                    SELECT 1 FROM [AspNetRoleClaims] AS existing
                    WHERE existing.[RoleId] = procurement.[Id]
                      AND (existing.[ClaimType] = claim.[ClaimType] OR
                           (existing.[ClaimType] IS NULL AND claim.[ClaimType] IS NULL))
                      AND (existing.[ClaimValue] = claim.[ClaimValue] OR
                           (existing.[ClaimValue] IS NULL AND claim.[ClaimValue] IS NULL)));

                -- Avoid duplicate actioners if an action already allows both operational roles.
                DELETE FROM [WorkflowActionerTemplates]
                WHERE [ActionerType] = 'Role' AND UPPER([ActionerKey]) = 'CATALOG_MANAGER'
                  AND EXISTS (
                    SELECT 1 FROM [WorkflowActionerTemplates] AS replacement
                    WHERE replacement.[ActionTemplateId] = [WorkflowActionerTemplates].[ActionTemplateId]
                      AND replacement.[ActionerType] = 'Role'
                      AND UPPER(replacement.[ActionerKey]) = 'PROCUREMENT_OFFICER');
                UPDATE [WorkflowActionerTemplates] SET [ActionerKey] = 'PROCUREMENT_OFFICER'
                WHERE [ActionerType] = 'Role' AND UPPER([ActionerKey]) = 'CATALOG_MANAGER';

                DELETE FROM [WorkflowActionerInstances]
                WHERE [ActionerType] = 'Role' AND UPPER([ActionerKey]) = 'CATALOG_MANAGER'
                  AND EXISTS (
                    SELECT 1 FROM [WorkflowActionerInstances] AS replacement
                    WHERE replacement.[ActionInstanceId] = [WorkflowActionerInstances].[ActionInstanceId]
                      AND replacement.[ActionerType] = 'Role'
                      AND UPPER(replacement.[ActionerKey]) = 'PROCUREMENT_OFFICER');
                UPDATE [WorkflowActionerInstances] SET [ActionerKey] = 'PROCUREMENT_OFFICER'
                WHERE [ActionerType] = 'Role' AND UPPER([ActionerKey]) = 'CATALOG_MANAGER';

                DELETE FROM [AspNetUserRoles]
                WHERE [RoleId] IN (SELECT [Id] FROM [AspNetRoles] WHERE [NormalizedName] = 'CATALOG_MANAGER');
                DELETE FROM [AspNetRoleClaims]
                WHERE [RoleId] IN (SELECT [Id] FROM [AspNetRoles] WHERE [NormalizedName] = 'CATALOG_MANAGER');
                DELETE FROM [AspNetRoles] WHERE [NormalizedName] = 'CATALOG_MANAGER';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "The original Catalog Manager memberships and workflow assignments cannot be inferred after consolidation.");
        }
    }
}
