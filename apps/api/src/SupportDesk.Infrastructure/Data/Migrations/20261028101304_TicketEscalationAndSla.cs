using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportDesk.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TicketEscalationAndSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SlaStartedAtUtc",
                table: "Tickets",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Tickets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("UPDATE [Tickets] SET [SlaStartedAtUtc] = [CreatedAtUtc] WHERE [DueAtUtc] IS NOT NULL;");
            migrationBuilder.Sql("UPDATE [Tickets] SET [Version] = NEWID();");
            migrationBuilder.Sql("""
                CREATE FUNCTION [dbo].[TicketSlaStatus](
                    @due datetime2(7), @resolved datetime2(7), @started datetime2(7),
                    @now datetime2(7), @threshold decimal(18,8))
                RETURNS int WITH SCHEMABINDING
                AS
                BEGIN
                    RETURN CASE
                        WHEN @due IS NULL THEN 0
                        WHEN @resolved IS NOT NULL THEN CASE WHEN @resolved <= @due THEN 4 ELSE 3 END
                        WHEN @now > @due THEN 3
                        WHEN @started IS NOT NULL AND
                            CONVERT(decimal(38,8), DATEDIFF_BIG(nanosecond, @now, @due)) * 100
                            <= CONVERT(decimal(38,8), DATEDIFF_BIG(nanosecond, @started, @due)) * @threshold THEN 2
                        ELSE 1
                    END;
                END
                """);

            migrationBuilder.CreateTable(
                name: "TicketEscalations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    FromPriority = table.Column<int>(type: "int", nullable: false),
                    ToPriority = table.Column<int>(type: "int", nullable: false),
                    FromAgentId = table.Column<int>(type: "int", nullable: true),
                    ToAgentId = table.Column<int>(type: "int", nullable: true),
                    FromDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ToDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EscalatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EscalatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketEscalations", x => x.Id);
                    table.CheckConstraint("CK_TicketEscalations_Priorities", "[FromPriority] BETWEEN 1 AND 3 AND [ToPriority] = [FromPriority] + 1");
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Agents_FromAgentId",
                        column: x => x.FromAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Agents_ToAgentId",
                        column: x => x.ToAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_FromAgentId",
                table: "TicketEscalations",
                column: "FromAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_Ticket_History",
                table: "TicketEscalations",
                columns: new[] { "TicketId", "EscalatedAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_ToAgentId",
                table: "TicketEscalations",
                column: "ToAgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION [dbo].[TicketSlaStatus];");
            migrationBuilder.DropTable(
                name: "TicketEscalations");

            migrationBuilder.DropColumn(
                name: "SlaStartedAtUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Tickets");
        }
    }
}
