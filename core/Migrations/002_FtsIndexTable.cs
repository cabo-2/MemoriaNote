using Microsoft.EntityFrameworkCore.Migrations;

namespace MemoriaNote.Migrations
{
    public partial class FtsIndexTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var ftsIndexCreateSql = @"
                CREATE VIRTUAL TABLE FtsIndex
                USING fts5(GuidAsString, Title, TagsAsString, Text, content='Pages', content_rowid='Rowid', tokenize='unicode61')";
            migrationBuilder.Sql(ftsIndexCreateSql); // FTS table
            //Triggers to keep the FTS index up to date.
            var pagesInsertSql = @"
                CREATE TRIGGER Pages_Insert AFTER INSERT ON Pages BEGIN
                INSERT INTO FtsIndex(rowid, GuidAsString, Title, TagsAsString, Text) VALUES(new.Rowid, new.GuidAsString, new.Title, new.TagsAsString, new.Text);
                INSERT INTO Contents(Rowid, GuidAsString, Title, 'Index', TagsAsString, Noteid, ContentType, CreateTime, UpdateTime, IsErased)
                VALUES (new.Rowid, new.GuidAsString, new.Title, new.'Index', new.TagsAsString, new.Noteid, new.ContentType, new.CreateTime, new.UpdateTime, new.IsErased);
                END;";
            migrationBuilder.Sql(pagesInsertSql);
            var pagesDeleteSql = @"
                CREATE TRIGGER Pages_Delete AFTER DELETE ON Pages BEGIN
                INSERT INTO FtsIndex(FtsIndex, rowid, GuidAsString, Title, TagsAsString, Text) VALUES('delete', old.Rowid, old.GuidAsString, old.Title, old.TagsAsString, old.Text);              
                DELETE FROM Contents WHERE Rowid = old.Rowid;
                END;";
            migrationBuilder.Sql(pagesDeleteSql);
            var pagesUpdateSql = @"
                CREATE TRIGGER Pages_Update AFTER UPDATE ON Pages BEGIN
                INSERT INTO FtsIndex(FtsIndex, rowid, GuidAsString, Title, TagsAsString, Text) VALUES('delete', old.Rowid, old.GuidAsString, old.Title, old.TagsAsString, old.Text);
                INSERT INTO FtsIndex(rowid, GuidAsString, Title, TagsAsString, Text) VALUES(new.Rowid, new.GuidAsString, new.Title, new.TagsAsString, new.Text);
                DELETE FROM Contents WHERE Rowid = old.Rowid;
                INSERT INTO Contents(Rowid, GuidAsString, Title, 'Index', TagsAsString, Noteid, ContentType, CreateTime, UpdateTime, IsErased)
                VALUES (new.Rowid, new.GuidAsString, new.Title, new.'Index', new.TagsAsString, new.Noteid, new.ContentType, new.CreateTime, new.UpdateTime, new.IsErased);
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
