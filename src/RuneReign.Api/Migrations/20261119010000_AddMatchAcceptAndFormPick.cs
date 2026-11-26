using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(RuneDbContext))]
    [Migration("20261119010000_AddMatchAcceptAndFormPick")]
    public partial class AddMatchAcceptAndFormPick : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "AcceptedA", table: "CombatMatches", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "AcceptedB", table: "CombatMatches", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(name: "AcceptDeadlineUtc", table: "CombatMatches", type: "datetime(6)", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PendingFormA", table: "CombatMatches", type: "longtext", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PendingFormB", table: "CombatMatches", type: "longtext", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AcceptedA", table: "CombatMatches");
            migrationBuilder.DropColumn(name: "AcceptedB", table: "CombatMatches");
            migrationBuilder.DropColumn(name: "AcceptDeadlineUtc", table: "CombatMatches");
            migrationBuilder.DropColumn(name: "PendingFormA", table: "CombatMatches");
            migrationBuilder.DropColumn(name: "PendingFormB", table: "CombatMatches");
        }
    }
}
