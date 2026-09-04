using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BulkReversal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveReferenceUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReversalTransactions_SessionIdOrFtReference",
                table: "ReversalTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_ReversalTransactions_SessionIdOrFtReference_Active",
                table: "ReversalTransactions",
                column: "SessionIdOrFtReference",
                unique: true,
                filter: "[Status] IN (N'Submitted', N'Processing', N'Reversed') OR ([Status] IS NULL AND [RowValidationStatus] = N'Valid')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReversalTransactions_SessionIdOrFtReference_Active",
                table: "ReversalTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_ReversalTransactions_SessionIdOrFtReference",
                table: "ReversalTransactions",
                column: "SessionIdOrFtReference");
        }
    }
}
