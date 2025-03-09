using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElasticSearchPdfApi.Migrations
{
    /// <inheritdoc />
    public partial class As : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Discriminator", table: "AspNetRoles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Discriminator",
                table: "AspNetRoles",
                type: "character varying(13)",
                maxLength: 13,
                nullable: false,
                defaultValue: ""
            );
        }
    }
}
