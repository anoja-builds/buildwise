import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import MaterialRequestsPage from './MaterialRequestsPage';
import { materialRequestService } from '../services/materialRequestService';

let roles = [];
vi.mock('../../../auth/AuthContext', () => ({ useAuth: () => ({ hasRole: role => roles.includes(role) }) }));
vi.mock('../services/materialRequestService', () => ({ materialRequestService: { getRequests: vi.fn(), approveRequest: vi.fn() } }));
vi.mock('../components/ProcurementPlanningPanel', () => ({ default: () => null }));
vi.mock('../components/CreateMaterialRequestModal', () => ({ default: () => null }));
const pending = { id: 42, projectName: 'Live project', reason: 'Site demand', status: 'PendingApproval', requiredDate: '2026-10-01', items: [{ materialName: 'Live material', quantity: 12, materialUnit: 'kg' }] };
beforeEach(() => {
  roles = ['ProjectManager'];
  vi.clearAllMocks();
  materialRequestService.getRequests.mockResolvedValue([pending]);
  materialRequestService.approveRequest.mockResolvedValue({ status: 'Approved' });
});

describe('material request decisions', () => {
  it.each(['ProjectManager', 'Administrator'])('allows %s to review and refreshes persisted status', async role => {
    roles = [role];
    const user = userEvent.setup();
    render(<MaterialRequestsPage />);
    await user.click(await screen.findByRole('button', { name: 'Review request #42' }));
    expect(screen.getByText('12 kg')).toBeInTheDocument();
    materialRequestService.getRequests.mockResolvedValue([{ ...pending, status: 'Approved' }]);
    await user.click(screen.getByRole('button', { name: 'Confirm decision' }));
    await waitFor(() => expect(materialRequestService.approveRequest).toHaveBeenCalledWith(42, { decision: 'Approved' }));
    expect(await screen.findByText('Approved', { selector: 'span' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Review request #42' })).not.toBeInTheDocument();
  });

  it.each(['SiteEngineer', 'ProcurementOfficer', 'ReceivingOfficer'])('does not expose approval for %s', async role => {
    roles = [role];
    render(<MaterialRequestsPage />);
    await screen.findByText('Live project');
    expect(screen.queryByRole('button', { name: 'Review request #42' })).not.toBeInTheDocument();
  });

  it('preserves the review and reports a failed rejection without pretending success', async () => {
    const user = userEvent.setup();
    materialRequestService.approveRequest.mockRejectedValue(new Error('Request was already reviewed'));
    render(<MaterialRequestsPage />);
    await user.click(await screen.findByRole('button', { name: 'Review request #42' }));
    await user.selectOptions(screen.getByLabelText('Decision'), 'Rejected');
    await user.click(screen.getByRole('button', { name: 'Confirm decision' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Request was already reviewed');
    expect(materialRequestService.approveRequest).toHaveBeenCalledWith(42, { decision: 'Rejected' });
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('uses the backend pending status in the filter', async () => {
    const user = userEvent.setup();
    render(<MaterialRequestsPage />);
    await screen.findByText('Live project');
    await user.selectOptions(screen.getByRole('combobox'), 'PendingApproval');
    await waitFor(() => expect(materialRequestService.getRequests).toHaveBeenLastCalledWith('PendingApproval'));
  });
});
