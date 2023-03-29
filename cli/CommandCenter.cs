using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Reactive.Concurrency;
using ReactiveUI;
using System.Collections.Generic;
using Terminal.Gui;
using McMaster.Extensions.CommandLineUtils;

namespace MemoriaNote.Cli
{
    public class CommandCenter
    {
        public int Edit(string name = null)
        {
            try
            {
                Configuration.Instance = ConfigurationCli.Create<ConfigurationCli>();

                var vm = new MemoriaNoteViewModel();
                vm.SearchEntry = name;

                var sc = new ScreenController();
                sc.RequestHome();

                sc.Start(vm);

                Configuration.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int New(string name)
        {
            try
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                Configuration.Instance = ConfigurationCli.Create<ConfigurationCli>();

                var vm = new MemoriaNoteViewModel();
                vm.EditingTitle = name;
                vm.EditingState = TextManageType.Create;

                var sc = new ScreenController();
                sc.RequestHome();
                sc.RequestEditor();

                sc.Start(vm);

                Configuration.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int ConfigEdit()
        {
            return 0;
        }

        public int ConfigShow()
        {
            return 0;
        }

        public int ConfigInit()
        {
            return 0;
        }

        public int Get(string name, string format = null)
        {
            try
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                Configuration.Instance = Configuration.Create<ConfigurationCli>();

                var vm = new MemoriaNoteViewModel();

                int skipCount = 0;
                int takeCount = Configuration.Instance.Search.MaxViewResultCount;

                var wg = vm.Workgroup;
                var result = wg.SearchContents(name, skipCount, takeCount, SearchRangeType.Note);

                foreach (var content in result.Contents)
                {
                    var page = wg.SelectedNote.Read(content);
                    Console.WriteLine(page.ToString());
                    break;
                }

                Configuration.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int List()
        {
            try
            {
                Configuration.Instance = Configuration.Create<ConfigurationCli>();
                var vm = new MemoriaNoteViewModel();
               
                var count = vm.Workgroup.SelectedNote.Count;
                var contents = vm.Workgroup.SelectedNote.GetContents(0, 1000);

                WriteLineList(contents, count);

                Configuration.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        static int GetIndexWidth(int viewCount)
        {
            int indexWidth = 0;
            while(viewCount > 0)
            {
                viewCount /= 10;      
                indexWidth++;
            }
            return indexWidth;
        }

        static int GetMaxNameLength(List<Content> list)
        {
            int textWidth = 0;
            foreach(var content in list)
            {
                if (content.Title.Length > textWidth)
                    textWidth = content.Title.Length;
            }
            return Math.Min(textWidth, 64);
        }

        static void AppendBoarder(StringBuilder buffer, int count)
        {
            foreach(var num in Enumerable.Range(0, count))
                buffer.Append("-");
        }

        static void WriteLineBoarder(int indexWidth, int textWidth)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.Append("+");
            AppendBoarder(buffer, indexWidth);
            buffer.Append("-+-");
            AppendBoarder(buffer, 7);
            buffer.Append("-+-");
            AppendBoarder(buffer, textWidth);
            buffer.Append("-+");
            Console.WriteLine(buffer.ToString());
        }

        static void WriteLineList(List<Content> contents, int totalCount)
        {
            if (totalCount < 0)
                throw new ArgumentException(nameof(totalCount));

            int num = 1;
            int indexWidth = GetIndexWidth(contents.Count);
            int nameWidth = GetMaxNameLength(contents);

            WriteLineBoarder(indexWidth, nameWidth);           
            foreach(var content in contents)
            {
                var buffer = new StringBuilder();
                buffer.Append("|");
                buffer.Append(num.ToString().PadLeft(indexWidth, '0'));
                buffer.Append(" | ");
                buffer.Append(content.Guid.ToHashId());
                buffer.Append(" | ");
                var name = content.ViewTitle;
                buffer.Append(name.Substring(0, Math.Min(name.Length, nameWidth)));
                foreach(var space in Enumerable.Repeat(" ", nameWidth - Math.Min(name.Length, nameWidth)))
                    buffer.Append(space);
                buffer.Append(" |");
                Console.WriteLine(buffer.ToString());   

                if (num >= contents.Count)
                    break;                    
                num++;          
            }
            WriteLineBoarder(indexWidth, nameWidth);
            if (contents.Count < totalCount)
                Console.WriteLine("Number of text messages exceeds 1000");
            
            Console.WriteLine("Total count: " + totalCount.ToString());
        }     

        public int Work()
        {
            return 0;
        }

        public int WorkSelect()
        {
            return 0;
        }

        public int WorkEdit()
        {
            return 0;
        }


        public int WorkCreate()
        {
            return 0;
        }

        public int WorkList()
        {
            try
            {
                Configuration.Instance = Configuration.Create<ConfigurationCli>();
                var vm = new MemoriaNoteViewModel();

                foreach(var note in vm.Workgroup.Notes)
                {
                    bool check = note == vm.Workgroup.SelectedNote;
                    StringBuilder buffer = new StringBuilder();
                    buffer.Append(" ");
                    buffer.Append(check ? "*" : " ");
                    buffer.Append(note.ToString());
                    Console.WriteLine(buffer.ToString());
                }

                Configuration.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int WorkAdd()
        {
            return 0;
        }

        public int WorkRemove()
        {
            return 0;
        }

        public int Import(string importDir)
        {
            try
            {
                if (importDir == null)
                    throw new ArgumentNullException(nameof(importDir));

                if (!Directory.Exists(importDir))
                {
                    Console.Error.WriteLine("Error: No such directory");
                    return -1;
                }

                Configuration.Instance = ConfigurationCli.Create<ConfigurationCli>();
                var vm = new MemoriaNoteViewModel();

                vm.Workgroup.SelectedNote.TextImporter(importDir).Wait();

                Console.WriteLine("Import completed");
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int Export(string exportDir)
        {
            try
            {
                if (exportDir == null)
                    throw new ArgumentNullException(nameof(exportDir));

                if (!Directory.Exists(exportDir))
                {
                    Console.Error.WriteLine("Error: No such directory");
                    return -1;
                }

                Configuration.Instance = ConfigurationCli.Create<ConfigurationCli>();
                var vm = new MemoriaNoteViewModel();

                vm.Workgroup.SelectedNote.TextExporter(exportDir).Wait();

                Console.WriteLine("Export completed");
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }
    }
}