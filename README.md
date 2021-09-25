# QBB-Perft
This project aims to compare the speed of different programming languages in a typical chess programming workload. It contains [Perft](https://www.chessprogramming.org/Perft) implementations in C and C# and Java that are based on the [QBBEngine](https://www.chessprogramming.org/QBBEngine) by Fabio Gobbato.

On Windows 10 using a Ryzen 5 3600 with a fixed clockrate of 4.2 Ghz I get the following performance results:

## C# version 1.4 with .Net 5
1361558651 Nodes, 28708ms, 47427K NPS

## Java version with JDK 17
1361558651 Nodes, 28095ms, 48462K NPS

## C version built with GCC via MinGW 64bit (gcc -Ofast -march=native -static)
1361558651 Nodes, 18963ms, 71800K NPS
