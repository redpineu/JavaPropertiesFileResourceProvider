using System;
using System.Collections.Generic;
using System.Linq;
using Babylon.ResourcesProvider;
using System.IO;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace JavaPropertiesFileResourceProvider
{
    /// <summary>
    /// The provider will read all Java .properties files in the base directory and treat them as files containing string resources.
    /// The provider assumes that invariant strings are contained in a file with no culture codes in the file name (e.g. strings.properties).
    /// All files containing culture codes of the form _culturecode-countrycode (e.g. strings_de-DE.json) will be treated as translations.
    /// Strings not present in the invariant file are ignored.
    /// Relative paths are fully supported. Subfolders of the base directory are also processed. The name of the subfolder becomes part
    /// of the resource name and therefore all translations of an invariant file must be placed in the same folder.
    /// Only the default ISO-8859-1 (Latin1) encoding is support. No escaping will be done. Any comments or other lines in the files will be stripped.
    /// </summary>
    public class JavaPropertiesFileResourceProvider : IResourcesProvider
    {
        const string localeRegex = @"_([a-z]{2,}-[A-Z]{2,}|[a-z]{2,})";

        string _storageLocation;

        /// <summary>
        /// The StorageLocation will be set by the user when creating a new generic localization project in Babylon.NET. It can be a path to a folder, a file name,
        /// a database connection string or any other information needed to access the resource files.
        /// </summary>
        public string StorageLocation
        {
            get
            {
                return _storageLocation;
            }

            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(value);

                _storageLocation = value;
            }
        }

        /// <summary>
        /// This text is displayed to the user as label to the storage location textbox/combobox when setting up the resource provider.
        /// </summary>
        public string StorageLocationUserText
        {
            get
            {
                return "Base Directory where language files are located";
            }
        }

        /// <summary>
        /// This is the type of storage used be the provider. Depending on the type Babylon.NET will display a FileSelectionControl, a DirectorySelectionControl 
        /// or a simple TextBox as StorageLocation input control.
        /// </summary>
        public StorageType StorageType
        {
            get
            {
                return StorageType.Directory;
            }
        }

        /// <summary>
        /// This is the description of the Resource Provider Babylon.NET will display when selecting a Resource Provider
        /// </summary>
        public string Description
        {
            get
            {
                return "Standard Java Properties File Resources Provider. Every file contains one language.";
            }
        }

        /// <summary>
        /// This is the name of the Resource Provider Babylon.NET will display when selecting a Resource Provider
        /// </summary>
        public string Name
        {
            get
            {
                return "Java Properties File Resources Provider";
            }
        }

        /// <summary>
        /// Babylon.NET will pass the path to the current solution to the provider. This can for example be used to work with relative paths.
        /// </summary>
        public string SolutionPath { get; set; }

        /// <summary>
        /// Babylon.NET will call this method when the resource files should be written.
        /// </summary>
        /// <param name="projectName">Name of the project whose resources are exported.</param>
        /// <param name="resourceStrings">A list of resource strings with related translations.</param>
        /// <param name="resultDelegate">Delegate to return the status of the export.</param>
        public void ExportResourceStrings(string projectName, ICollection<StringResource> resourceStrings, ResourceStorageOperationResultDelegate resultDelegate)
        {
            // We use a dictionary as cache for the resources for each file
            Dictionary<string, ICollection<StringResource>> fileCache = new Dictionary<string, ICollection<StringResource>>();

            // We keep an error list with files that cannot be written to avoid the same error over and over
            List<string> errorList = new List<string>();

            // convert relative storage location into absolute one
            string baseDirectory = GetBaseDirectory();

            // loop over all strings...
            foreach (var resString in resourceStrings)
            {
                // ... and all locales. Babylon.NET uses an empty string as locale for the invariant language.
                foreach (string locale in resString.GetLocales())
                {
                    // assemble file name
                    string formattedLocale = ('_' + locale).TrimEnd(new char[] { '_' });
                    string filename = Path.Combine(baseDirectory, string.Format("{0}{1}.properties", resString.StorageLocation, formattedLocale));

                    // if we have this file on the error list skip it
                    if (errorList.Contains(filename))
                    {
                        continue;
                    }

                    // check if we have the file in our cache
                    if (!fileCache.ContainsKey(filename))
                    {
                        // load strings from file if file exists 
                        if (File.Exists(filename))
                        {
                            try
                            {
                                using (StreamReader fileStream = File.OpenText(filename))
                                {
                                    var strings = ReadResourceStrings(filename);
                                    fileCache.Add(filename, strings);
                                }
                            }
                            catch(Exception ex)
                            {
                                if (resultDelegate != null)
                                {
                                    ResourceStorageOperationResultItem resultItem = new ResourceStorageOperationResultItem(filename);
                                    resultItem.ProjectName = projectName;
                                    resultItem.Result = ResourceStorageOperationResult.Error;
                                    resultItem.Message = ex.GetBaseException().Message;
                                    resultDelegate(resultItem);
                                }

                                errorList.Add(filename);

                                continue;
                            }
                        }
                        else
                        {
                            // create dictionary for new file
                            var strings = new List<StringResource>();
                            fileCache.Add(filename, strings);
                        }
                    }

                    // update the string
                    var stringResources = fileCache[filename];
                    var s = stringResources.FirstOrDefault(sr => sr.Name == resString.Name);
                    if (s == null)
                    {
                        s = new StringResource(resString.Name, "");
                        stringResources.Add(s);
                    }

                    s.SetLocaleText(locale, resString.GetLocaleText(locale));
                    s.Notes = resString.Notes;
                }
            }

            // save all dictionaries in cache
            foreach (var item in fileCache)
            {
                ResourceStorageOperationResultItem resultItem = new ResourceStorageOperationResultItem(item.Key);
                resultItem.ProjectName = projectName;

                // get locale from file name
                string locale = Regex.Match(item.Key, localeRegex).Value.TrimStart(new char[] { '_' });

                try
                {
                    WriteResourceStrings(item, locale);

                    // report success
                    resultDelegate?.Invoke(resultItem);
                }
                catch (Exception ex)
                {
                    // report error
                    if (resultDelegate != null)
                    {
                        resultItem.Result = ResourceStorageOperationResult.Error;
                        resultItem.Message = ex.GetBaseException().Message;
                        resultDelegate(resultItem);
                    }
                }
            }
        }

        /// <summary>
        /// Called by Babylon.NET when synchronizing a project with the respective resource files.
        /// </summary>
        /// <param name="projectName">Name of the project whose resources are exported.</param>
        /// <returns></returns>
        public ICollection<StringResource> ImportResourceStrings(string projectName)
        {
            // We use a Dictionary to keep a list of all StringResource object searchable by the key.
            Dictionary<string, StringResource> workingDictionary = new Dictionary<string, StringResource>();

            // convert relative storage location into absolute one
            string baseDirectory = GetBaseDirectory();
            Regex regex = new Regex(localeRegex);

            // iterate over the whole folder tree starting from the base directory.
            foreach (var file in Directory.EnumerateFiles(baseDirectory, "*.properties", SearchOption.AllDirectories))
            {
                var strings = ReadResourceStrings(file);

                // get locale from file name
                string locale = regex.Match(file).Value.TrimStart(new char[] { '_' });

                foreach (var resourceString in strings)
                {
                    string relativeDirectory = Path.GetDirectoryName(file).Substring(baseDirectory.Length).TrimStart(Path.DirectorySeparatorChar);

                    string plainFilename = Path.Combine(relativeDirectory, regex.Replace(Path.GetFileNameWithoutExtension(Path.GetFileName(file)),""));

                    // check whether we already have the string
                    StringResource targetResourceString;
                    if (!workingDictionary.TryGetValue(plainFilename + resourceString.Name, out targetResourceString))
                    {
                        targetResourceString = new StringResource(resourceString.Name, "");
                        resourceString.StorageLocation = plainFilename;
                        workingDictionary.Add(plainFilename + resourceString.Name, resourceString);
                    }

                    // add locale text. Babylon.NET uses an empty string as locale for the invariant language. A StringResource is only valid if the invariant language is set. 
                    // StringResources without an invariant language text are discared by Babylon.NET.
                    targetResourceString.SetLocaleText(locale, resourceString.GetLocaleText(locale));
                }
            }

            // get collection of stringResources
            List<StringResource> result = new List<StringResource>();
            workingDictionary.ToList().ForEach(i => result.Add(i.Value));
            return result;
        }

        private ICollection<StringResource> ReadResourceStrings(string filename)
        {
            var result = new List<StringResource>();

            using (var sr = new System.IO.StreamReader(filename))
            {
                string line = "";
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith("!") || !line.Contains("="))
                    {
                        continue; // skip comments or lines without '='
                    }

                    var keyvalue = line.Split(new char[] { '=' }, 2);
                    if (keyvalue.Length < 2)
                    {
                        continue;   // skip invalid lines
                    }

                    string key = keyvalue[0].Trim();
                    string value = keyvalue[1].TrimStart();

                    StringResource stringRes = new StringResource(key, "");

                    // get locale from file name
                    Regex regex = new Regex(localeRegex);
                    var match = regex.Match(filename);
                    string locale = match.Value.TrimStart(new char[] { '_' });

                    // add locale text
                    stringRes.SetLocaleText(locale, value);

                    result.Add(stringRes);
                }
            }

            return result;
        }

        private static void WriteResourceStrings(KeyValuePair<string, ICollection<StringResource>> item, string locale)
        {
            using (StreamWriter fileStream = File.CreateText(item.Key))
            {
                foreach (var s in item.Value)
                {
                    fileStream.WriteLine(s.Name + " = " + s.GetLocaleText(locale));
                }
            }
        }

        private string GetBaseDirectory()
        {
            string baseDirectory = _storageLocation;
            if (!Path.IsPathRooted(baseDirectory))
            {
                baseDirectory = Path.GetFullPath(Path.Combine(SolutionPath, baseDirectory));
            }

            return baseDirectory;
        }
    }
}
