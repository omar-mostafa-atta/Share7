using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Narrows the purchase idempotency index to **completed** transactions.
    /// <para>
    /// It was unique across every state, which meant a refused attempt permanently burned its
    /// <c>requestId</c>: a student told "not enough coins" who topped up and tapped buy again got
    /// their own earlier refusal replayed back, forever. Idempotency is there to stop a second
    /// <em>charge</em>, and a refusal never made one.
    /// </para>
    /// <para>
    /// Safe on existing data without any cleanup — the old index was stricter, so no row can already
    /// violate the new one.
    /// </para>
    /// </summary>
    public partial class PurchaseIdempotencyOnCompletedOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseTransactions_UserId_RequestId",
                table: "PurchaseTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseTransactions_UserId_RequestId",
                table: "PurchaseTransactions",
                columns: new[] { "UserId", "RequestId" },
                unique: true,
                filter: "[State] = 'COMPLETED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseTransactions_UserId_RequestId",
                table: "PurchaseTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseTransactions_UserId_RequestId",
                table: "PurchaseTransactions",
                columns: new[] { "UserId", "RequestId" },
                unique: true);
        }
    }
}
