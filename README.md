<p align="center">
  <img src="https://dot-sys.github.io/VortexCSRSSTool/Assets/VortexLogo.svg" alt="Vortex Logo" width="100" height="100">
</p>

<h2 align="center">Vortex FAT Analyzer</h2>

<p align="center">
  Standalone tool for raw FAT16/32 & exFAT parsing. Uncover deleted files with metadata and recover them!<br><br>
  ⭐ Star this project if you found it useful.
</p>

---

### Overview

**Vortex FAT Analyzer** delivers low-level FAT/exFAT forensics in a standalone .NET 4.6.2 EXE for Win10/11. Parse boot sectors, reconstructs Metadata and pull deleted files from unallocated space - all live, no dumps needed. Supports: FAT16, FAT32, exFAT

#### File Enumeration

Shows live and deleted files: paths, timestamps, attributes, signatures. Visually shows anomalies: red for deleted/replaced, gold for unsigned, blue for hidden.

#### Deleted Scanning

Raw disk reads target root directories and clusters, identifies 0xE5 deleted markers. Detects overwrites by filename and size matching.

#### Recovery

Carves data from contiguous and chained clusters to files. Batch recovery mode available. Handles partial recoveries and large files intelligently.

---

### Features

- **Standalone EXE**: All dependencies embedded
- **No Installation**: Run directly from USB
- **FAT/exFAT Analysis**: Real-time parsing without modifications
- **VirusTotal Hash Links**: Auto Hash-Carving for VT checks
- **Batch Recovery**: Multi-File recovery from raw clusters with detailed logs

### Requirements

- .NET Framework 4.6.2
- Windows 10 or Windows 11
- Administrator privileges (required for raw disk access)
