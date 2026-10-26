# Native SQLite-Abhängigkeit

SQLite 3.53.4; Ziel x86_64-linux-gnu.2.17. SQLite ist eine separate Abhängigkeit für Economy und Surf. Die vorhandene Binärdatei ist im vollständigen Release-Quellpaket enthalten.

Quelle: https://www.sqlite.org/2026/sqlite-amalgamation-3530400.zip

SHA-256: `1E71DDF93849C6A6ECF58B827C0692073D2DD7EE40196158068F7B29F422E87D`.

Compiler: https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip

SHA-256: `554F5378228923FFD558EAC35E21AF020C73789D87AFEABF4BFD16F2E6FEED2C`.

Archive nach erfolgreicher Prüfsummenkontrolle entpacken. Mit dem enthaltenen Zig-Compiler:

```text
zig cc -target x86_64-linux-gnu.2.17 -shared -fPIC -O2 -DSQLITE_THREADSAFE=1 -DSQLITE_ENABLE_COLUMN_METADATA -DSQLITE_ENABLE_FTS5 -DSQLITE_ENABLE_RTREE -DSQLITE_ENABLE_MATH_FUNCTIONS -DSQLITE_ENABLE_DBSTAT_VTAB sqlite3.c -o libe_sqlite3.so -lm -ldl -lpthread
```

Die Ausgabe unter `Addons/LPLK_Economy/Rohskripte/native/libe_sqlite3.so` ablegen. Das bereitgestellte Binary hat SHA-256 `3DF9102A69C7D0F3C803CD0C4CA7F11877DDA4741A5D854BC44E713B0BF9F329`. Unterschiedliche Buildpfade können die Bytegleichheit eines eigenen Builds beeinflussen; erforderlich ist dieselbe Linux-x64-/glibc-2.17-Zielkompatibilität. Keine Systembibliothek auf dem Gameserver ersetzen.
