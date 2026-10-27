PRAGMA application_id = 1782832723;
PRAGMA user_version = 2;

CREATE TABLE "Metadata" (
    "Key" TEXT NOT NULL CONSTRAINT "PK_Metadata" PRIMARY KEY,
    "Value" TEXT NULL
);

CREATE TABLE "Pages" (
    "InternalId" INTEGER NOT NULL CONSTRAINT "PK_Pages" PRIMARY KEY AUTOINCREMENT,
    "PageId" TEXT NOT NULL COLLATE BINARY,
    "Name" TEXT NOT NULL COLLATE BINARY,
    "CreateTime" TEXT NOT NULL,
    "UpdateTime" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    "Tags" TEXT NOT NULL,
    "Text" TEXT NOT NULL,
    CONSTRAINT "CK_Pages_InternalId_Positive" CHECK (InternalId > 0),
    CONSTRAINT "CK_Pages_PageId_Canonical" CHECK (
        length(PageId) = 36
        AND PageId = lower(PageId)
        AND substr(PageId, 9, 1) = '-'
        AND substr(PageId, 14, 1) = '-'
        AND substr(PageId, 19, 1) = '-'
        AND substr(PageId, 24, 1) = '-'
        AND replace(PageId, '-', '') NOT GLOB '*[^0-9a-f]*'
        AND PageId <> '00000000-0000-0000-0000-000000000000'
    ),
    CONSTRAINT "CK_Pages_Name_Length" CHECK (length(Name) BETWEEN 1 AND 255),
    CONSTRAINT "CK_Pages_Name_OuterSpace" CHECK (Name = trim(Name)),
    CONSTRAINT "CK_Pages_CreateTime_Canonical" CHECK (
        length(CreateTime) = 27
        AND substr(CreateTime, 5, 1) = '-'
        AND substr(CreateTime, 8, 1) = '-'
        AND substr(CreateTime, 11, 1) = ' '
        AND substr(CreateTime, 14, 1) = ':'
        AND substr(CreateTime, 17, 1) = ':'
        AND substr(CreateTime, 20, 1) = '.'
        AND replace(replace(replace(replace(CreateTime, '-', ''), ' ', ''), ':', ''), '.', '')
            NOT GLOB '*[^0-9]*'
    ),
    CONSTRAINT "CK_Pages_UpdateTime_Canonical" CHECK (
        length(UpdateTime) = 27
        AND substr(UpdateTime, 5, 1) = '-'
        AND substr(UpdateTime, 8, 1) = '-'
        AND substr(UpdateTime, 11, 1) = ' '
        AND substr(UpdateTime, 14, 1) = ':'
        AND substr(UpdateTime, 17, 1) = ':'
        AND substr(UpdateTime, 20, 1) = '.'
        AND replace(replace(replace(replace(UpdateTime, '-', ''), ' ', ''), ':', ''), '.', '')
            NOT GLOB '*[^0-9]*'
    ),
    CONSTRAINT "CK_Pages_Revision_Positive" CHECK (Revision > 0),
    CONSTRAINT "CK_Pages_Tags_JsonArray" CHECK (
        json_valid(Tags)
        AND json_type(Tags) = 'array'
    )
);

CREATE UNIQUE INDEX "IX_Pages_PageId"
    ON "Pages" ("PageId" COLLATE BINARY);

CREATE UNIQUE INDEX "IX_Pages_Name"
    ON "Pages" ("Name" COLLATE BINARY);

CREATE TABLE "PageTags" (
    "PageInternalId" INTEGER NOT NULL,
    "Label" TEXT NOT NULL COLLATE BINARY,
    CONSTRAINT "PK_PageTags" PRIMARY KEY ("PageInternalId", "Label"),
    CONSTRAINT "FK_PageTags_Pages_PageInternalId" FOREIGN KEY ("PageInternalId")
        REFERENCES "Pages" ("InternalId") ON DELETE CASCADE,
    CONSTRAINT "CK_PageTags_Label_Length" CHECK (length(Label) BETWEEN 1 AND 64),
    CONSTRAINT "CK_PageTags_Label_OuterSpace" CHECK (Label = trim(Label))
) WITHOUT ROWID;

CREATE INDEX "IX_PageTags_Label_PageInternalId"
    ON "PageTags" ("Label" COLLATE BINARY, "PageInternalId");

CREATE TRIGGER "Pages_TagsInsert"
AFTER INSERT ON "Pages"
BEGIN
    INSERT INTO "PageTags"("PageInternalId", "Label")
        SELECT new.InternalId, value
        FROM json_each(new.Tags);
END;

CREATE TRIGGER "Pages_TagsUpdate"
AFTER UPDATE OF Tags ON "Pages"
WHEN old.Tags IS NOT new.Tags
BEGIN
    DELETE FROM "PageTags"
        WHERE "PageInternalId" = old.InternalId;
    INSERT INTO "PageTags"("PageInternalId", "Label")
        SELECT new.InternalId, value
        FROM json_each(new.Tags);
END;

CREATE VIRTUAL TABLE "FtsIndex"
    USING fts5(
        Name,
        Text,
        content='Pages',
        content_rowid='InternalId',
        tokenize='unicode61'
    );

CREATE TRIGGER "Pages_FtsInsert"
AFTER INSERT ON "Pages"
BEGIN
    INSERT INTO "FtsIndex"(rowid, Name, Text)
        VALUES (new.InternalId, new.Name, new.Text);
END;

CREATE TRIGGER "Pages_FtsDelete"
AFTER DELETE ON "Pages"
BEGIN
    INSERT INTO "FtsIndex"("FtsIndex", rowid, Name, Text)
        VALUES ('delete', old.InternalId, old.Name, old.Text);
END;

CREATE TRIGGER "Pages_FtsUpdate"
AFTER UPDATE OF Name, Text ON "Pages"
WHEN old.Name IS NOT new.Name
  OR old.Text IS NOT new.Text
BEGIN
    INSERT INTO "FtsIndex"("FtsIndex", rowid, Name, Text)
        VALUES ('delete', old.InternalId, old.Name, old.Text);
    INSERT INTO "FtsIndex"(rowid, Name, Text)
        VALUES (new.InternalId, new.Name, new.Text);
END;

CREATE TRIGGER "Pages_RejectInternalIdUpdate"
BEFORE UPDATE OF InternalId ON "Pages"
WHEN old.InternalId IS NOT new.InternalId
BEGIN
    SELECT RAISE(ABORT, 'Pages.InternalId is immutable');
END;

CREATE TRIGGER "Pages_RejectPageIdUpdate"
BEFORE UPDATE OF PageId ON "Pages"
WHEN old.PageId IS NOT new.PageId
BEGIN
    SELECT RAISE(ABORT, 'Pages.PageId is immutable');
END;
