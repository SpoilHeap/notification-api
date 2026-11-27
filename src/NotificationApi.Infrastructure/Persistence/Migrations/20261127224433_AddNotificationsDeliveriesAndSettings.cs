using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsDeliveriesAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ChannelOverride = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    SmsToProtectedApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    SmsToApiKeyHint = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    SmsToSenderId = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.CheckConstraint("CK_Settings_SingleRow", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "Deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Contact = table.Column<string>(type: "TEXT", maxLength: 70, nullable: false),
                    Via = table.Column<string>(type: "TEXT", maxLength: 70, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    NextAttemptAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deliveries_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_NotificationId_Position",
                table: "Deliveries",
                columns: new[] { "NotificationId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_Status_NextAttemptAt",
                table: "Deliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IdempotencyKey_CreatedAt",
                table: "Notifications",
                columns: new[] { "IdempotencyKey", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deliveries");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
