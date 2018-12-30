# JAVA Properties File Resource Provider for Babylon

The provider will read all Java .properties files in the base directory and treat them as files containing string resources.
The provider assumes that invariant strings are contained in a file with no culture codes in the file name (e.g. strings.properties).
All files containing culture codes of the form _culturecode-countrycode (e.g. strings_de-DE.json) will be treated as translations.

Strings not present in the invariant file are ignored.

Relative paths are fully supported. Subfolders of the base directory are also processed. The name of the subfolder becomes part
of the resource name and therefore all translations of an invariant file must be placed in the same folder.

Only the default ISO-8859-1 (Latin1) encoding is support. No escaping will be done. Any comments or other lines in the files will be stripped.
Comments are not supported.
    