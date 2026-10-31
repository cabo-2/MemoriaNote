DELETE FROM "PageTags";

INSERT INTO "PageTags"("PageInternalId", "Label")
    SELECT "Pages"."InternalId", json_each.value
    FROM "Pages", json_each("Pages"."Tags");
