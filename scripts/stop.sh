#!/usr/bin/env bash
# Close Cosmoteer on the PC.
source "$(dirname "$0")/pc.sh"
pc 'Get-Process Cosmoteer -ErrorAction SilentlyContinue | Stop-Process -Force; "stopped"'
