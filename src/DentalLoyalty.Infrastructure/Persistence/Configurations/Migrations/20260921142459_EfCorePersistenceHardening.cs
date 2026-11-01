using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalLoyalty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EfCorePersistenceHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Services",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Rewards",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Rewards",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "Patients",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "Patients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "Patients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Patients",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Clinics",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Services_ClinicId",
                table: "Services",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Rewards_ClinicId",
                table: "Rewards",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardRedemptions_ClinicId_RedeemedAt",
                table: "RewardRedemptions",
                columns: new[] { "ClinicId", "RedeemedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RewardRedemptions_PatientId_RedeemedAt",
                table: "RewardRedemptions",
                columns: new[] { "PatientId", "RedeemedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RewardRedemptions_PatientId_RewardId",
                table: "RewardRedemptions",
                columns: new[] { "PatientId", "RewardId" });

            migrationBuilder.CreateIndex(
                name: "IX_RewardRedemptions_RewardId",
                table: "RewardRedemptions",
                column: "RewardId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ClinicId_CreatedAt",
                table: "Referrals",
                columns: new[] { "ClinicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferredPatientId",
                table: "Referrals",
                column: "ReferredPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferrerPatientId",
                table: "Referrals",
                column: "ReferrerPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientServices_ClinicId_UsedAt",
                table: "PatientServices",
                columns: new[] { "ClinicId", "UsedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientServices_PatientId_UsedAt",
                table: "PatientServices",
                columns: new[] { "PatientId", "UsedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientServices_ServiceId",
                table: "PatientServices",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId_CreatedAt",
                table: "Patients",
                columns: new[] { "ClinicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyAccounts_ClinicId",
                table: "LoyaltyAccounts",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyAccounts_PatientId",
                table: "LoyaltyAccounts",
                column: "PatientId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LoyaltyAccounts_Clinics_ClinicId",
                table: "LoyaltyAccounts",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LoyaltyAccounts_Patients_PatientId",
                table: "LoyaltyAccounts",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Clinics_ClinicId",
                table: "Patients",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientServices_Clinics_ClinicId",
                table: "PatientServices",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientServices_Patients_PatientId",
                table: "PatientServices",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientServices_Services_ServiceId",
                table: "PatientServices",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Clinics_ClinicId",
                table: "Referrals",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Patients_ReferredPatientId",
                table: "Referrals",
                column: "ReferredPatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Patients_ReferrerPatientId",
                table: "Referrals",
                column: "ReferrerPatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RewardRedemptions_Clinics_ClinicId",
                table: "RewardRedemptions",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RewardRedemptions_Patients_PatientId",
                table: "RewardRedemptions",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RewardRedemptions_Rewards_RewardId",
                table: "RewardRedemptions",
                column: "RewardId",
                principalTable: "Rewards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Rewards_Clinics_ClinicId",
                table: "Rewards",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Clinics_ClinicId",
                table: "Services",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoyaltyAccounts_Clinics_ClinicId",
                table: "LoyaltyAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_LoyaltyAccounts_Patients_PatientId",
                table: "LoyaltyAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Clinics_ClinicId",
                table: "Patients");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientServices_Clinics_ClinicId",
                table: "PatientServices");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientServices_Patients_PatientId",
                table: "PatientServices");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientServices_Services_ServiceId",
                table: "PatientServices");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Clinics_ClinicId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Patients_ReferredPatientId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Patients_ReferrerPatientId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_RewardRedemptions_Clinics_ClinicId",
                table: "RewardRedemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RewardRedemptions_Patients_PatientId",
                table: "RewardRedemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RewardRedemptions_Rewards_RewardId",
                table: "RewardRedemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_Rewards_Clinics_ClinicId",
                table: "Rewards");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Clinics_ClinicId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Services_ClinicId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Rewards_ClinicId",
                table: "Rewards");

            migrationBuilder.DropIndex(
                name: "IX_RewardRedemptions_ClinicId_RedeemedAt",
                table: "RewardRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_RewardRedemptions_PatientId_RedeemedAt",
                table: "RewardRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_RewardRedemptions_PatientId_RewardId",
                table: "RewardRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_RewardRedemptions_RewardId",
                table: "RewardRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ClinicId_CreatedAt",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ReferredPatientId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ReferrerPatientId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_PatientServices_ClinicId_UsedAt",
                table: "PatientServices");

            migrationBuilder.DropIndex(
                name: "IX_PatientServices_PatientId_UsedAt",
                table: "PatientServices");

            migrationBuilder.DropIndex(
                name: "IX_PatientServices_ServiceId",
                table: "PatientServices");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId_CreatedAt",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyAccounts_ClinicId",
                table: "LoyaltyAccounts");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyAccounts_PatientId",
                table: "LoyaltyAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Services",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Rewards",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Rewards",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "Patients",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "Patients",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "Patients",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Patients",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Clinics",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }
    }
}
