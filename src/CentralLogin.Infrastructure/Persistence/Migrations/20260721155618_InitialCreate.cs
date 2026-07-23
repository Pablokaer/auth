using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralLogin.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ApplicationUsers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                KeycloakSubject = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                EmailSnapshot = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastLoginAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ApplicationUsers_KeycloakSubject",
            table: "ApplicationUsers",
            column: "KeycloakSubject",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ApplicationUsers");
    }
}
