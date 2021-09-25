# QBB-Perft
Perft implementations based on QBBEngine by Fabio Gobbato in C and C# and Java

On Windows 10 using a Ryzen 5 3600 with a fixed clockrate of 4.2 Ghz I get the following performance results:

C# version 1.4 with .Net 5
Total: 1361558651 Nodes, 28708ms, 47427K NPS

Java version with JDK 17
Total: 1361558651 Nodes, 28095ms, 48462K NPS

C version compiled with "gcc -Ofast -march=native -static" using MinGW 64bit
Total: 1361558651 Nodes, 18963ms, 71800K NPS
