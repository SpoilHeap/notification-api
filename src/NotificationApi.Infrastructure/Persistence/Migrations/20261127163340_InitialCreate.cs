using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    ProtectedToken = table.Column<string>(type: "TEXT", nullable: false),
                    WebhookUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ProtectedWebhookSecret = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiClients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_Name",
                table: "ApiClients",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiClients");
        }
    }
}
