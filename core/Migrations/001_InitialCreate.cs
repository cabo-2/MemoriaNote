using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MemoriaNote.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contents",
                columns: table => new
                {
                    Rowid = table.Column<int>(nullable: false),
                    GuidAsString = table.Column<string>(nullable: false),
                    Title = table.Column<string>(nullable: true),
                    Index = table.Column<int>(nullable: false),
                    TagsAsString = table.Column<string>(nullable: true),
                    Noteid = table.Column<string>(nullable: false),
                    ContentType = table.Column<string>(nullable: true),
                    CreateTime = table.Column<DateTime>(nullable: false),
                    UpdateTime = table.Column<DateTime>(nullable: false),
                    IsErased = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contents", x => x.GuidAsString);
                });

            migrationBuilder.CreateTable(
                name: "History",
                columns: table => new
                {
                    Rowid = table.Column<int>(nullable: false),
                    Generation = table.Column<int>(nullable: false),
                    TitlePatch = table.Column<string>(nullable: true),
                    TitleHash = table.Column<string>(nullable: true),
                    TextPatch = table.Column<string>(nullable: true),
                    TextHash = table.Column<string>(nullable: true),
                    TagsPatch = table.Column<string>(nullable: true),
                    TagsHash = table.Column<string>(nullable: true),
                    CreateTime = table.Column<DateTime>(nullable: false),
                    UpdateTime = table.Column<DateTime>(nullable: false),
                    SaveTime = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => new { x.Rowid, x.Generation });
                });

            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    Rowid = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuidAsString = table.Column<string>(nullable: false),
                    Title = table.Column<string>(nullable: true),
                    Index = table.Column<int>(nullable: false),
                    TagsAsString = table.Column<string>(nullable: true),
                    Noteid = table.Column<string>(nullable: false),
                    ContentType = table.Column<string>(nullable: true),
                    CreateTime = table.Column<DateTime>(nullable: false),
                    UpdateTime = table.Column<DateTime>(nullable: false),
                    IsErased = table.Column<bool>(nullable: false),
                    Text = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.Rowid);
                    table.UniqueConstraint("AK_Pages_GuidAsString", x => x.GuidAsString);
                });

            migrationBuilder.CreateTable(
                name: "TitlePage",
                columns: table => new
                {
                    Key = table.Column<string>(nullable: false),
                    Value = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitlePage", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_Title_Index",
                table: "Contents",
                columns: new[] { "Title", "Index" });

            migrationBuilder.CreateIndex(
                name: "IX_Pages_Title_Index",
                table: "Pages",
                columns: new[] { "Title", "Index" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contents");

            migrationBuilder.DropTable(
                name: "History");

            migrationBuilder.DropTable(
                name: "Pages");

            migrationBuilder.DropTable(
                name: "TitlePage");
        }
    }
}
