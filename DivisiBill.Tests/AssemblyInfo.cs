using System.Runtime.InteropServices;

[assembly: ComVisible(false)]
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.ClassLevel)]