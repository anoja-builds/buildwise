SELECT w."Id", w."Objective", w."Status", w."ApprovalStatus", w."InitiatedByUserId", w."MaterialRequestId", count(s."Id") AS steps, string_agg(s."AgentRole", ' -> ' ORDER BY s."StepOrder") AS audit_path
FROM agent_workflows w
LEFT JOIN agent_workflow_steps s ON s."AgentWorkflowId" = w."Id"
WHERE w."Objective" LIKE 'Identify planning risks%'
GROUP BY w."Id", w."Objective", w."Status", w."ApprovalStatus", w."InitiatedByUserId", w."MaterialRequestId"
ORDER BY w."Id" DESC
LIMIT 3;