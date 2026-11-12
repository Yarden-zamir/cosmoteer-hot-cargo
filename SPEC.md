# Hot Cargo — game design spec and plan

Cosmoteer career mod. Target game version: 0.30.5a (assembly version 1.0.0, .NET 10).
Status: implemented in one batch. Tuning and playtests are open (section 18, step 8).

The sections are files in `spec/`, one per section, named `<number>-<name>.md` and read in number order. A new feature gets a new file with the next free number and a new requirement number range. Requirement numbers are unique across all files: find one with `grep -rn '^<number>\. ' spec/`.
