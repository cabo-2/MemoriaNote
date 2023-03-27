using System;
using System.IO;
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
            return 0;
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
            return 0;
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