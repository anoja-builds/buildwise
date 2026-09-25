"""Load the repository-root .env file for standalone agent execution.

The normal scripts/start-dev.ps1 path also loads .env into process variables.
This module makes `python quotation_agent.py` and direct uvicorn commands use the
same local configuration without committing secrets.
"""

from pathlib import Path

from dotenv import load_dotenv

ROOT_ENV = Path(__file__).resolve().parents[2] / ".env"
load_dotenv(ROOT_ENV, override=False)
