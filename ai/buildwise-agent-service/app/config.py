import os
from dataclasses import dataclass
from dotenv import load_dotenv

load_dotenv()
# Never export model messages or tool observations to hosted tracing automatically.
os.environ['LANGSMITH_TRACING'] = 'false'
os.environ['LANGCHAIN_TRACING_V2'] = 'false'


@dataclass(frozen=True)
class Settings:
    api_key: str
    service_key: str
    model: str = 'gemini-2.5-flash'
    max_iterations: int = 6
    max_tool_calls: int = 6
    timeout_seconds: float = 90


def get_settings() -> Settings:
    model = os.getenv('CHAT_MODEL', 'gemini-2.5-flash').strip()
    if not model or len(model) > 100:
        raise ValueError('CHAT_MODEL must contain 1 to 100 characters')
    return Settings(api_key=os.getenv('GOOGLE_API_KEY', '').strip(),
                    service_key=os.getenv('QUALITY_AGENT_SERVICE_KEY', '').strip(), model=model)
