using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobRadar.Matching.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "matching");

            migrationBuilder.CreateTable(
                name: "profiles",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Skills = table.Column<List<string>>(type: "text[]", nullable: false),
                    Seniorities = table.Column<string[]>(type: "text[]", nullable: false),
                    RemoteOnly = table.Column<bool>(type: "boolean", nullable: false),
                    Locations = table.Column<List<string>>(type: "text[]", nullable: false),
                    SalaryFloorUsd = table.Column<int>(type: "integer", nullable: true),
                    MaxYearsAsked = table.Column<int>(type: "integer", nullable: true),
                    ExcludedTitleWords = table.Column<List<string>>(type: "text[]", nullable: false),
                    NotifyThreshold = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scored_postings",
                schema: "matching",
                columns: table => new
                {
                    PostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Reasons = table.Column<string>(type: "jsonb", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnnouncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scored_postings", x => x.PostingId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scored_postings_Score",
                schema: "matching",
                table: "scored_postings",
                column: "Score");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profiles",
                schema: "matching");

            migrationBuilder.DropTable(
                name: "scored_postings",
                schema: "matching");
        }
    }
}
