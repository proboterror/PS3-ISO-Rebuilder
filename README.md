# Command Line / Console version of PS3-ISO-Rebuilder

PS3 ISO Rebuilder: Creates PS3 non-encrypted iso from folder and IRD disc layout description.

Based on PS3-ISO-Rebuilder v1.0.4.1 by jonnysp 2014 [C# source](https://github.com/ifcaro/PS3-ISO-Rebuilder) reversed from .NET assembly by [ifcaro](https://github.com/ifcaro).

Usage: PS3-ISO-Rebuilder &lt;path_to_ird&gt; &lt;path_to_jb_folder&gt; &lt;path_to_output_iso&gt;

Returns 1 if source files verification / image creation / checksum fails, return 0 for successful completion.<br>
Error messages are written to stderr.

iso image built with plain header.

In order to generate Redump.org verified encrypted iso image you need [PS3Dec](https://github.com/al3xtjames/PS3Dec) utility and dkey / disc key for iso encryption.