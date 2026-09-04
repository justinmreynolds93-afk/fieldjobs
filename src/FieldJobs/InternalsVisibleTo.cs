using System.Runtime.CompilerServices;

// Lets FieldJobs.Tests build a schema against a throwaway SQLite connection
// (Db.Schema) without exposing it to real consumers of the assembly.
[assembly: InternalsVisibleTo("FieldJobs.Tests")]
