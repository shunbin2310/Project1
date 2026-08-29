using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Project1.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseRequestWorkflowEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequesterName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    RequiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowProcessTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowProcessTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequestItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    EstimatedUnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestItems_PurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowProcessInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessTemplateId = table.Column<int>(type: "int", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    CurrentStepInstanceId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowProcessInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowProcessInstances_WorkflowProcessTemplates_ProcessTemplateId",
                        column: x => x.ProcessTemplateId,
                        principalTable: "WorkflowProcessTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStepTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessTemplateId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsInitial = table.Column<bool>(type: "bit", nullable: false),
                    IsTerminal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStepTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStepTemplates_WorkflowProcessTemplates_ProcessTemplateId",
                        column: x => x.ProcessTemplateId,
                        principalTable: "WorkflowProcessTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessInstanceId = table.Column<int>(type: "int", nullable: false),
                    ActionInstanceId = table.Column<int>(type: "int", nullable: true),
                    FromStepCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ToStepCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActionBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowHistory_WorkflowProcessInstances_ProcessInstanceId",
                        column: x => x.ProcessInstanceId,
                        principalTable: "WorkflowProcessInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStepInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessInstanceId = table.Column<int>(type: "int", nullable: false),
                    SourceStepTemplateId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsInitial = table.Column<bool>(type: "bit", nullable: false),
                    IsTerminal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStepInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStepInstances_WorkflowProcessInstances_ProcessInstanceId",
                        column: x => x.ProcessInstanceId,
                        principalTable: "WorkflowProcessInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromStepTemplateId = table.Column<int>(type: "int", nullable: false),
                    ToStepTemplateId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowActionTemplates_WorkflowStepTemplates_FromStepTemplateId",
                        column: x => x.FromStepTemplateId,
                        principalTable: "WorkflowStepTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowActionTemplates_WorkflowStepTemplates_ToStepTemplateId",
                        column: x => x.ToStepTemplateId,
                        principalTable: "WorkflowStepTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromStepInstanceId = table.Column<int>(type: "int", nullable: false),
                    ToStepInstanceId = table.Column<int>(type: "int", nullable: false),
                    SourceActionTemplateId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowActionInstances_WorkflowStepInstances_FromStepInstanceId",
                        column: x => x.FromStepInstanceId,
                        principalTable: "WorkflowStepInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowActionInstances_WorkflowStepInstances_ToStepInstanceId",
                        column: x => x.ToStepInstanceId,
                        principalTable: "WorkflowStepInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionerTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionTemplateId = table.Column<int>(type: "int", nullable: false),
                    ActionerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActionerKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionerTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowActionerTemplates_WorkflowActionTemplates_ActionTemplateId",
                        column: x => x.ActionTemplateId,
                        principalTable: "WorkflowActionTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionerInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionInstanceId = table.Column<int>(type: "int", nullable: false),
                    ActionerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActionerKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionerInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowActionerInstances_WorkflowActionInstances_ActionInstanceId",
                        column: x => x.ActionInstanceId,
                        principalTable: "WorkflowActionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "WorkflowProcessTemplates",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "EntityType", "IsActive", "IsPublished", "Name", "PublishedAtUtc", "Version" },
                values: new object[] { 1, "PURCHASE_REQUEST", new DateTimeOffset(new DateTime(2026, 8, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PurchaseRequest", true, true, "Purchase Request Approval", new DateTimeOffset(new DateTime(2026, 8, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1 });

            migrationBuilder.InsertData(
                table: "WorkflowStepTemplates",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsInitial", "IsTerminal", "Name", "ProcessTemplateId" },
                values: new object[,]
                {
                    { 1, "DRAFT", 1, true, false, "Draft", 1 },
                    { 2, "DEPARTMENT_REVIEW", 2, false, false, "Department Review", 1 },
                    { 3, "FINANCE_REVIEW", 3, false, false, "Finance Review", 1 },
                    { 4, "APPROVED", 4, false, true, "Approved", 1 },
                    { 5, "REJECTED", 5, false, true, "Rejected", 1 }
                });

            migrationBuilder.InsertData(
                table: "WorkflowActionTemplates",
                columns: new[] { "Id", "Code", "FromStepTemplateId", "Name", "RequiresComment", "ToStepTemplateId" },
                values: new object[,]
                {
                    { 1, "SUBMIT", 1, "Submit", false, 2 },
                    { 2, "APPROVE", 2, "Approve department review", false, 3 },
                    { 3, "REJECT", 2, "Reject department review", true, 5 },
                    { 4, "APPROVE", 3, "Approve finance review", false, 4 },
                    { 5, "REJECT", 3, "Reject finance review", true, 5 }
                });

            migrationBuilder.InsertData(
                table: "WorkflowActionerTemplates",
                columns: new[] { "Id", "ActionTemplateId", "ActionerKey", "ActionerType" },
                values: new object[,]
                {
                    { 1, 1, null, "Requester" },
                    { 2, 2, "DEPARTMENT_APPROVER", "Role" },
                    { 3, 3, "DEPARTMENT_APPROVER", "Role" },
                    { 4, 4, "FINANCE_APPROVER", "Role" },
                    { 5, 5, "FINANCE_APPROVER", "Role" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestItems_ProductId",
                table: "PurchaseRequestItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestItems_PurchaseRequestId_ProductId",
                table: "PurchaseRequestItems",
                columns: new[] { "PurchaseRequestId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_DepartmentId",
                table: "PurchaseRequests",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_RequestNumber",
                table: "PurchaseRequests",
                column: "RequestNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionerInstances_ActionInstanceId",
                table: "WorkflowActionerInstances",
                column: "ActionInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionerTemplates_ActionTemplateId",
                table: "WorkflowActionerTemplates",
                column: "ActionTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionInstances_FromStepInstanceId_Code",
                table: "WorkflowActionInstances",
                columns: new[] { "FromStepInstanceId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionInstances_ToStepInstanceId",
                table: "WorkflowActionInstances",
                column: "ToStepInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionTemplates_FromStepTemplateId_Code",
                table: "WorkflowActionTemplates",
                columns: new[] { "FromStepTemplateId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionTemplates_ToStepTemplateId",
                table: "WorkflowActionTemplates",
                column: "ToStepTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistory_ProcessInstanceId_ActionAtUtc",
                table: "WorkflowHistory",
                columns: new[] { "ProcessInstanceId", "ActionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessInstances_EntityType_EntityId",
                table: "WorkflowProcessInstances",
                columns: new[] { "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessInstances_ProcessTemplateId",
                table: "WorkflowProcessInstances",
                column: "ProcessTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessInstances_Status_CurrentStepInstanceId",
                table: "WorkflowProcessInstances",
                columns: new[] { "Status", "CurrentStepInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessTemplates_Code_Version",
                table: "WorkflowProcessTemplates",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessTemplates_EntityType_IsPublished_IsActive",
                table: "WorkflowProcessTemplates",
                columns: new[] { "EntityType", "IsPublished", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepInstances_ProcessInstanceId_Code",
                table: "WorkflowStepInstances",
                columns: new[] { "ProcessInstanceId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepTemplates_ProcessTemplateId_Code",
                table: "WorkflowStepTemplates",
                columns: new[] { "ProcessTemplateId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseRequestItems");

            migrationBuilder.DropTable(
                name: "WorkflowActionerInstances");

            migrationBuilder.DropTable(
                name: "WorkflowActionerTemplates");

            migrationBuilder.DropTable(
                name: "WorkflowHistory");

            migrationBuilder.DropTable(
                name: "PurchaseRequests");

            migrationBuilder.DropTable(
                name: "WorkflowActionInstances");

            migrationBuilder.DropTable(
                name: "WorkflowActionTemplates");

            migrationBuilder.DropTable(
                name: "WorkflowStepInstances");

            migrationBuilder.DropTable(
                name: "WorkflowStepTemplates");

            migrationBuilder.DropTable(
                name: "WorkflowProcessInstances");

            migrationBuilder.DropTable(
                name: "WorkflowProcessTemplates");
        }
    }
}
