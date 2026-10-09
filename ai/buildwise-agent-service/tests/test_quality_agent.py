import asyncio
import copy
import json
from pathlib import Path
import unittest
from unittest.mock import patch
from fastapi.testclient import TestClient
from langchain_core.messages import AIMessage, ToolMessage
from pydantic import ValidationError
from app.config import Settings, get_settings
from app.main import app
from app.models import EvidencePackage, QualityRiskRecommendation
from app.quality_agent import analyse, SYSTEM
from app.tools import build_tools

FIXTURES = Path(__file__).parent / 'fixtures'
EVIDENCE = json.loads((FIXTURES / 'evidence.json').read_text(encoding='utf-8-sig'))
RECOMMENDATION = json.loads((FIXTURES / 'recommendation.json').read_text(encoding='utf-8-sig'))


def call(name, args=None):
    return AIMessage(content='', tool_calls=[{'name': name, 'args': args or {}, 'id': name + '-call'}])


def evidence_turns():
    return [call('get_current_inspection_evidence'), call('get_supplier_quality_history', {'limit': 10}),
            call('get_prior_non_conformance_summary')]


class FakeModel:
    def __init__(self, turns):
        self.turns = iter(turns)
        self.inputs = []

    def bind_tools(self, tools):
        self.tools = tools
        return self

    async def ainvoke(self, messages):
        self.inputs.append(list(messages))
        return next(self.turns)


class ContractTests(unittest.TestCase):
    def test_valid_request_and_recommendation(self):
        EvidencePackage.model_validate(EVIDENCE)
        QualityRiskRecommendation.model_validate(RECOMMENDATION)

    def test_invalid_requests(self):
        variants = []
        bad = copy.deepcopy(EVIDENCE); bad['items'][0]['acceptedQuantity'] = 11; variants.append(bad)
        bad = copy.deepcopy(EVIDENCE); bad['history'] = [{}] * 21; variants.append(bad)
        bad = copy.deepcopy(EVIDENCE); bad['items'] = []; variants.append(bad)
        bad = copy.deepcopy(EVIDENCE); bad['sql'] = 'SELECT'; variants.append(bad)
        for bad in variants:
            with self.subTest(bad=bad), self.assertRaises(ValidationError):
                EvidencePackage.model_validate(bad)

    def test_invalid_output_fields(self):
        for change in [{'overallDecision': 'Accepted'}, {'riskLevel': 'Safe'}, {'evidenceSummary': 'x' * 2001}, {'ncrRecommended': 'true'}]:
            with self.subTest(change=change), self.assertRaises(ValidationError):
                QualityRiskRecommendation.model_validate(RECOMMENDATION | change)

    def test_ncr_requires_all_suggestions(self):
        fields = ['suggestedSeverity', 'suggestedIssueDescription', 'suggestedCorrectiveAction']
        for missing in [fields, *[[field] for field in fields]]:
            bad = copy.deepcopy(RECOMMENDATION)
            for field in missing:
                bad['itemRecommendations'][0][field] = None
            with self.subTest(missing=missing), self.assertRaises(ValidationError):
                QualityRiskRecommendation.model_validate(bad)

    def test_ncr_rejects_blank_issue_description(self):
        bad = copy.deepcopy(RECOMMENDATION)
        bad['itemRecommendations'][0]['suggestedIssueDescription'] = ' \t\n '
        with self.assertRaises(ValidationError):
            QualityRiskRecommendation.model_validate(bad)

    def test_ncr_rejects_blank_corrective_action(self):
        bad = copy.deepcopy(RECOMMENDATION)
        bad['itemRecommendations'][0]['suggestedCorrectiveAction'] = ' \t\n '
        with self.assertRaises(ValidationError):
            QualityRiskRecommendation.model_validate(bad)

    def test_no_ncr_rejects_each_nonnull_suggestion(self):
        fields = ['suggestedSeverity', 'suggestedIssueDescription', 'suggestedCorrectiveAction']
        for supplied in fields:
            bad = copy.deepcopy(RECOMMENDATION)
            bad['ncrRecommended'] = False
            item = bad['itemRecommendations'][0]
            item['ncrRecommended'] = False
            for field in fields:
                if field != supplied:
                    item[field] = None
            with self.subTest(supplied=supplied), self.assertRaises(ValidationError):
                QualityRiskRecommendation.model_validate(bad)

    def test_no_ncr_accepts_null_suggestions_without_repair(self):
        valid = copy.deepcopy(RECOMMENDATION)
        valid['ncrRecommended'] = False
        item = valid['itemRecommendations'][0]
        item['ncrRecommended'] = False
        for field in ['suggestedSeverity', 'suggestedIssueDescription', 'suggestedCorrectiveAction']:
            item[field] = None
        self.assertEqual(QualityRiskRecommendation.model_validate(valid).model_dump(), valid)

    def test_blank_evidence_summary(self):
        with self.assertRaises(ValidationError):
            QualityRiskRecommendation.model_validate(RECOMMENDATION | {'evidenceSummary': ' \t\n '})

    def test_blank_rationale_summary(self):
        with self.assertRaises(ValidationError):
            QualityRiskRecommendation.model_validate(RECOMMENDATION | {'rationaleSummary': ' \t\n '})

    def test_blank_item_rationale_and_risk_flag(self):
        for collection, field in [('itemRecommendations', 'rationale'), ('riskFlags', 'flag')]:
            bad = copy.deepcopy(RECOMMENDATION)
            bad[collection][0][field] = ' \t\n '
            with self.subTest(field=field), self.assertRaises(ValidationError):
                QualityRiskRecommendation.model_validate(bad)

    def test_tools_are_scoped_and_validate_arguments(self):
        tools = build_tools(EvidencePackage.model_validate(EVIDENCE))
        self.assertEqual(len(tools), 3)
        for limit in [0, 21, '20']:
            with self.assertRaises(ValidationError):
                tools['get_supplier_quality_history'].invoke({'limit': limit})
        with self.assertRaises(ValidationError):
            tools['get_current_inspection_evidence'].args_schema.model_validate({'supplierId': 999})
        self.assertEqual(tools['get_current_inspection_evidence'].invoke({})['supplierId'], 3)


class GraphTests(unittest.IsolatedAsyncioTestCase):
    async def test_real_graph_mock_model_cycle(self):
        model = FakeModel(evidence_turns() + [call('QualityRiskRecommendation', RECOMMENDATION)])
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'), model)
        self.assertTrue(result.success)
        self.assertEqual(result.iterationCount, 4)
        self.assertEqual(len(result.trace), 4)
        self.assertIsInstance(model.inputs[1][-1], ToolMessage)
        self.assertNotIn('Ignore previous', model.inputs[0][-1].content)
        self.assertIn('untrustedEvidence', model.inputs[1][-1].content)
        self.assertIn('UNTRUSTED DATA', SYSTEM)
        self.assertNotIn('Ignore previous', result.model_dump_json())

    async def test_missing_key(self):
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'))
        self.assertEqual(result.errorCode, 'missing_api_key')

    async def test_no_tool_shortcut(self):
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'),
            FakeModel([call('QualityRiskRecommendation', RECOMMENDATION)]))
        self.assertEqual(result.errorCode, 'insufficient_tool_use')

    async def test_unknown_tool(self):
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'), FakeModel([call('execute_sql')]))
        self.assertEqual(result.errorCode, 'invalid_tool_call')

    async def test_invalid_tool_arguments(self):
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'),
            FakeModel([call('get_supplier_quality_history', {'limit': 21})]))
        self.assertEqual(result.errorCode, 'invalid_tool_call')

    async def test_iteration_limit(self):
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'),
            FakeModel([call('get_current_inspection_evidence') for _ in range(8)]))
        self.assertEqual(result.errorCode, 'iteration_limit')
        self.assertEqual(result.iterationCount, 6)

    async def test_tool_call_limit(self):
        calls = [{'name': 'get_current_inspection_evidence', 'args': {}, 'id': str(i)} for i in range(7)]
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'),
            FakeModel([AIMessage(content='', tool_calls=calls)]))
        self.assertEqual(result.errorCode, 'iteration_limit')
        self.assertEqual(len(result.trace), 6)

    async def test_invalid_final(self):
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'),
            FakeModel(evidence_turns() + [AIMessage(content='not json')]))
        self.assertEqual(result.errorCode, 'invalid_output')
        self.assertIsNone(result.recommendation)

    async def test_timeout(self):
        class Slow(FakeModel):
            async def ainvoke(self, messages):
                await asyncio.sleep(1)
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test', timeout_seconds=0.01), Slow([]))
        self.assertEqual(result.errorCode, 'timeout')

    async def test_provider_failure_is_sanitized(self):
        class Broken(FakeModel):
            async def ainvoke(self, messages):
                raise RuntimeError('secret-api-key')
        result = await analyse(EvidencePackage.model_validate(EVIDENCE), Settings('', 'test'), Broken([]))
        self.assertFalse(result.success)
        self.assertNotIn('secret-api-key', result.model_dump_json())


class ApiTests(unittest.TestCase):
    def setUp(self):
        app.dependency_overrides[get_settings] = lambda: Settings('', 'internal-test-key')
        self.client = TestClient(app)
        self.client.__enter__()

    def tearDown(self):
        self.client.__exit__(None, None, None)
        app.dependency_overrides.clear()

    def test_health(self):
        self.assertEqual(self.client.get('/health').status_code, 200)

    def test_service_auth(self):
        self.assertEqual(self.client.post('/quality-risk/analyse', json=EVIDENCE).status_code, 401)

    def test_pydantic_request_error_does_not_echo_evidence(self):
        response = self.client.post('/quality-risk/analyse', headers={'X-Quality-Agent-Key': 'internal-test-key'},
            json=EVIDENCE | {'execute': 'secret-content'})
        self.assertEqual(response.status_code, 422)
        self.assertNotIn('secret-content', response.text)

    def test_missing_provider_key_is_failure(self):
        response = self.client.post('/quality-risk/analyse', headers={'X-Quality-Agent-Key': 'internal-test-key'}, json=EVIDENCE)
        self.assertEqual(response.status_code, 200)
        self.assertFalse(response.json()['success'])
        self.assertEqual(response.json()['errorCode'], 'missing_api_key')


if __name__ == '__main__':
    unittest.main()
