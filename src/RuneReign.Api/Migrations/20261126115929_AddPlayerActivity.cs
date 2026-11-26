using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Players",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAtUtc",
                table: "Players",
                type: "timestamp without time zone",
                nullable: true);

            // Players who existed before this change: use when their account was first set up, or today.
            migrationBuilder.Sql("UPDATE \"Players\" SET \"CreatedAtUtc\" = COALESCE(\"RealmInitializedAtUtc\", timezone('utc', now()));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "Players");
        }
    }
}
