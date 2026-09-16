import secrets
from fastapi import FastAPI, Depends, Header, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from .config import Settings, get_settings
from .models import EvidencePackage, AgentResult
from .quality_agent import analyse

app = FastAPI(title='BuildWise Internal Quality Agent', docs_url=None, redoc_url=None, openapi_url=None)


@app.exception_handler(RequestValidationError)
async def invalid_request(request: Request, exc: RequestValidationError):
    # Do not echo untrusted evidence or request values in error bodies.
    return JSONResponse(status_code=422, content={'detail': 'Invalid bounded evidence package.'})


def authorize(x_quality_agent_key: str | None = Header(default=None),
              settings: Settings = Depends(get_settings)) -> Settings:
    if not settings.service_key:
        raise HTTPException(status_code=503, detail='Internal service authentication is not configured.')
    if not x_quality_agent_key or not secrets.compare_digest(x_quality_agent_key, settings.service_key):
        raise HTTPException(status_code=401, detail='Invalid internal service credentials.')
    return settings


@app.get('/health')
def health():
    return {'status': 'ok', 'service': 'BuildWise internal quality agent'}


@app.post('/quality-risk/analyse', response_model=AgentResult)
async def analyse_quality(evidence: EvidencePackage, settings: Settings = Depends(authorize)):
    return await analyse(evidence, settings)
