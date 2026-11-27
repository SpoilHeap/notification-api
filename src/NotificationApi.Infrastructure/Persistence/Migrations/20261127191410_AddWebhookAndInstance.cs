using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookAndInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WebhookRegisteredAt",
                table: "ApiClients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Instance",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RecipientsRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instance", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Instance");

            migrationBuilder.DropColumn(
                name: "WebhookRegisteredAt",
                table: "ApiClients");
        }
    }
}
