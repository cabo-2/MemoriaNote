using System;
using System.Collections.Generic;
using DiffMatchPatch;

namespace MemoriaNote
{
    public class PageHistory
    {
        public int Rowid { get; set; }
        public int Generation { get; set; }
        public string TitlePatch { get; set; }
        public string TitleHash { get; set; }
        public string TextPatch { get; set; }
        public string TextHash { get; set; }
        public string TagsPatch { get; set; }
        public string TagsHash { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public DateTime SaveTime { get; set; }

        public static PageHistory Create(Page oldPage, Page newPage)
        {
            if (oldPage.Rowid != newPage.Rowid)
                throw new ArgumentException("Guid does not match");

            var diff = new diff_match_patch();
            var history = new PageHistory();
            history.Rowid = oldPage.Rowid;
            history.Generation = 0;
            history.CreateTime = oldPage.CreateTime;
            history.UpdateTime = oldPage.UpdateTime;
            history.SaveTime = DateTime.UtcNow;
            history.TitlePatch =
                diff.patch_toText(diff.patch_make(newPage.Title, oldPage.Title));
            history.TitleHash = oldPage.Title.CalculateHash();
            history.TextPatch =
                diff.patch_toText(diff.patch_make(newPage.Text, oldPage.Text));
            history.TextHash = oldPage.Text.CalculateHash();
            history.TagsPatch =
                diff.patch_toText(diff.patch_make(newPage.TagsAsString, oldPage.TagsAsString));
            history.TagsHash = oldPage.TagsAsString.CalculateHash();
            return history;
        }

        public Page Restore(Page newPage)
        {
            if (this.Rowid != newPage.Rowid)
                throw new ArgumentException("Guid does not match");

            var diff = new diff_match_patch();
            var oldPage = new Page();
            oldPage.Rowid = newPage.Rowid;
            oldPage.Guid = newPage.Guid;
            oldPage.Index = newPage.Index;
            oldPage.ContentType = newPage.ContentType;
            oldPage.Parent = newPage.Parent;
            oldPage.UpdateTime = this.UpdateTime;
            oldPage.CreateTime = this.CreateTime;
            oldPage.Title =
                (diff.patch_apply(diff.patch_fromText(this.TitlePatch), newPage.Title))[0] as string;
            oldPage.Text = 
                (diff.patch_apply(diff.patch_fromText(this.TextPatch), newPage.Text))[0] as string;
            oldPage.TagsAsString =
                (diff.patch_apply(diff.patch_fromText(this.TagsPatch), newPage.TagsAsString))[0] as string;

            ValidateHash(oldPage);
            return oldPage;
        }

        protected void ValidateHash(Page page)
        {
            if (this.TitleHash != page.Title.CalculateHash())
                throw new InvalidOperationException("title");
            if (this.TextHash != page.Text.CalculateHash())
                throw new InvalidOperationException("text");
            if (this.TagsHash != page.TagsAsString.CalculateHash())
                throw new InvalidOperationException("tags");
        }

        public List<Diff> GetTextDiff(Page newPage)
        {
            var diff = new diff_match_patch();
            var oldText =
                (diff.patch_apply(diff.patch_fromText(this.TextPatch), newPage.Text))[0] as string;

            return diff.diff_main(oldText, newPage.Text);
        }
    }
}
