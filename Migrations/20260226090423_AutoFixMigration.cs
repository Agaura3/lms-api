using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lms_api.Migrations
{
    /// <inheritdoc />
    public partial class AutoFixMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_CompanyId",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId_Department",
                table: "Users",
                columns: new[] { "CompanyId", "Department" });

            migrationBuilder.CreateIndex(
                name: "IX_Leaves_CompanyId_LeaveType",
                table: "Leaves",
                columns: new[] { "CompanyId", "LeaveType" });

            migrationBuilder.CreateIndex(
                name: "IX_Leaves_CompanyId_StartDate_Status",
                table: "Leaves",
                columns: new[] { "CompanyId", "StartDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Leaves_CompanyId_Status",
                table: "Leaves",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailQueues_Status_RetryCount",
                table: "EmailQueues",
                columns: new[] { "Status", "RetryCount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_CompanyId_Department",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Leaves_CompanyId_LeaveType",
                table: "Leaves");

            migrationBuilder.DropIndex(
                name: "IX_Leaves_CompanyId_StartDate_Status",
                table: "Leaves");

            migrationBuilder.DropIndex(
                name: "IX_Leaves_CompanyId_Status",
                table: "Leaves");

            migrationBuilder.DropIndex(
                name: "IX_EmailQueues_Status_RetryCount",
                table: "EmailQueues");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId",
                table: "Users",
                column: "CompanyId");
        }
    }
}
