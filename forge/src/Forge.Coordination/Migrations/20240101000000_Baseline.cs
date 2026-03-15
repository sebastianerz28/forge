using Microsoft.EntityFrameworkCore.Migrations;

namespace Forge.Coordination.Migrations;

/// <summary>
/// Empty baseline migration — schema already exists from Python migrations.
/// EF Core tracks future changes from this baseline.
/// </summary>
public partial class Baseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Schema already exists from factory/migrations/001_initial_schema.sql
        // and factory/migrations/002_add_runner_name.sql.
        // This migration exists only so EF Core has a baseline snapshot.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No-op: we don't want to drop the existing schema.
    }
}
