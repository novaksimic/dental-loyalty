using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalLoyalty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientServiceRecordingAndTransactionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecordedAt",
                table: "PatientServices",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Before this column, a service was always recorded at the moment it
            // was stored as performed (UsedAt was set on recording), so that is
            // exactly when existing services were recorded.
            migrationBuilder.Sql(
                """
                UPDATE [PatientServices] SET [RecordedAt] = [UsedAt];
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedBy",
                table: "PatientServices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PatientServiceId",
                table: "LoyaltyTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientServices_RecordedBy",
                table: "PatientServices",
                column: "RecordedBy");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_PatientServiceId",
                table: "LoyaltyTransactions",
                column: "PatientServiceId",
                unique: true,
                filter: "[PatientServiceId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_LoyaltyTransactions_PatientServices_PatientServiceId",
                table: "LoyaltyTransactions",
                column: "PatientServiceId",
                principalTable: "PatientServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientServices_Users_RecordedBy",
                table: "PatientServices",
                column: "RecordedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoyaltyTransactions_PatientServices_PatientServiceId",
                table: "LoyaltyTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientServices_Users_RecordedBy",
                table: "PatientServices");

            migrationBuilder.DropIndex(
                name: "IX_PatientServices_RecordedBy",
                table: "PatientServices");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyTransactions_PatientServiceId",
                table: "LoyaltyTransactions");

            migrationBuilder.DropColumn(
                name: "RecordedAt",
                table: "PatientServices");

            migrationBuilder.DropColumn(
                name: "RecordedBy",
                table: "PatientServices");

            migrationBuilder.DropColumn(
                name: "PatientServiceId",
                table: "LoyaltyTransactions");
        }
    }
}
