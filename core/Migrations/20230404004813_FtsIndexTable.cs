using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoriaNote.Core.Migrations
{
    public partial class FtsIndexTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var ftsIndexCreateSql = @"
                CREATE VIRTUAL TABLE FtsIndex
                USING fts5(Uuid, Name, Tags, Text, content='Pages', content_rowid='Rowid', tokenize='unicode61')";
            migrationBuilder.Sql(ftsIndexCreateSql);
            var pagesInsertSql = @"
                CREATE TRIGGER Pages_Insert AFTER INSERT ON Pages BEGIN
                INSERT INTO FtsIndex(Rowid, Uuid, Name, Tags, Text) VALUES(new.Rowid, new.Uuid, new.Name, new.Tags, new.Text);
                INSERT INTO Contents(Rowid, Uuid, Name, 'Index', Tags, ContentType, CreateTime, UpdateTime, IsErased)
                VALUES (new.Rowid, new.Uuid, new.Name, new.'Index', new.Tags, new.ContentType, new.CreateTime, new.UpdateTime, new.IsErased);
                END;";
            migrationBuilder.Sql(pagesInsertSql);
            var pagesDeleteSql = @"
                CREATE TRIGGER Pages_Delete AFTER DELETE ON Pages BEGIN
                INSERT INTO FtsIndex(FtsIndex, Rowid, Uuid, Name, Tags, Text) VALUES('delete', old.Rowid, old.Uuid, old.Name, old.Tags, old.Text);              
                DELETE FROM Contents WHERE Uuid = old.Uuid;
                END;";
            migrationBuilder.Sql(pagesDeleteSql);
            var pagesUpdateSql = @"
                CREATE TRIGGER Pages_Update AFTER UPDATE ON Pages BEGIN
                INSERT INTO FtsIndex(FtsIndex, Rowid, Uuid, Name, Tags, Text) VALUES('delete', old.Rowid, old.Uuid, old.Name, old.Tags, old.Text);
                INSERT INTO FtsIndex(Rowid, Uuid, Name, Tags, Text) VALUES(new.Rowid, new.Uuid, new.Name, new.Tags, new.Text);
                DELETE FROM Contents WHERE Uuid = old.Uuid;
                INSERT INTO Contents(Rowid, Uuid, Name, 'Index', Tags, ContentType, CreateTime, UpdateTime, IsErased)
                VALUES (new.Rowid, new.Uuid, new.Name, new.'Index', new.Tags, new.ContentType, new.CreateTime, new.UpdateTime, new.IsErased);
                END;";
            migrationBuilder.Sql(pagesUpdateSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER Pages_Insert");
            migrationBuilder.Sql(@"DROP TRIGGER Pages_Update");
            migrationBuilder.Sql(@"DROP TRIGGER Pages_Delete");
            migrationBuilder.Sql(@"DROP TABLE FtsIndex");
        }
    }
}
