import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import DeliveryDashboard from './DeliveryDashboard';
import { deliveryService } from '../services/deliveryService';
import { useAuth } from '../../../auth/AuthContext';

vi.mock('../../../auth/AuthContext', () => ({ useAuth: vi.fn() }));

vi.mock('../services/deliveryService', () => ({
  deliveryService: {
    getExpectedDeliveries: vi.fn(),
    getDeliveryHistory: vi.fn(),
    getConfirmedPOs: vi.fn(),
    scheduleDelivery: vi.fn()
  }
}));

vi.mock('../components/DeliveryRiskPanel', () => ({ default: () => null }));
vi.mock('../components/DeliveryDiscrepancyPanel', () => ({ default: () => null }));
vi.mock('./RecordDeliveryForm', () => ({ default: () => null }));

describe('DeliveryDashboard - Schedule Delivery Integration', () => {
  const initialExpected = [
    {
      id: 1,
      deliveryReference: 'DEL-2026-001',
      supplierName: 'ABC Steel Ltd',
      projectName: 'Metro Station Project',
      status: 'Scheduled',
      purchaseOrderId: 101
    }
  ];

  const initialHistory = [
    {
      id: 2,
      deliveryReference: 'DEL-2026-002',
      supplierName: 'Lanka Cement Ltd',
      purchaseOrderId: 102,
      status: 'Received',
      receivedBy: 'Ramya',
      deliveryDate: '2026-09-25T10:00:00Z',
      items: []
    }
  ];

  const confirmedPOs = [
    {
      id: 103,
      supplierId: 5,
      supplierName: 'Global Aggregates',
      status: 'Confirmed'
    }
  ];

  beforeEach(() => {
    vi.clearAllMocks();
    useAuth.mockReturnValue({ roles: ['Administrator'] });
    deliveryService.getExpectedDeliveries.mockResolvedValue(initialExpected);
    deliveryService.getDeliveryHistory.mockResolvedValue(initialHistory);
    deliveryService.getConfirmedPOs.mockResolvedValue(confirmedPOs);
    deliveryService.scheduleDelivery.mockResolvedValue({ id: 3, deliveryReference: 'DEL-2026-003', status: 'Scheduled' });
  });

  it.each([
    ['SiteEngineer', true, false],
    ['QualityInspector', false, false],
    ['ProcurementOfficer', false, true],
    ['ProcurementManager', false, true],
    ['Administrator', true, true],
  ])('%s sees only permitted delivery actions', async (role, receive, schedule) => {
    useAuth.mockReturnValue({ roles: [role] });
    render(<DeliveryDashboard />);
    await screen.findByText('DEL-2026-001');
    expect(Boolean(screen.queryByRole('button', { name: 'Receive', exact: true }))).toBe(receive);
    expect(Boolean(screen.queryByRole('button', { name: /Schedule Delivery/ }))).toBe(schedule);
    expect(Boolean(screen.queryByRole('button', { name: 'AI Risk Check' }))).toBe(schedule);
    expect(deliveryService.getConfirmedPOs).not.toHaveBeenCalled();
  });

  it('renders the Schedule Delivery button in the header', async () => {
    render(<DeliveryDashboard />);
    const scheduleButton = await screen.findByRole('button', { name: /\+ Schedule Delivery/i });
    expect(scheduleButton).toBeInTheDocument();
  });

  it('opens the Schedule Delivery modal when clicking the button', async () => {
    const user = userEvent.setup();
    render(<DeliveryDashboard />);

    const scheduleButton = await screen.findByRole('button', { name: /\+ Schedule Delivery/i });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await user.click(scheduleButton);

    const modal = await screen.findByRole('dialog');
    expect(modal).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Schedule Delivery' })).toBeInTheDocument();
  });

  it('closes the modal when Cancel is clicked', async () => {
    const user = userEvent.setup();
    render(<DeliveryDashboard />);

    await user.click(await screen.findByRole('button', { name: /\+ Schedule Delivery/i }));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();

    const cancelButton = screen.getByRole('button', { name: 'Cancel' });
    await user.click(cancelButton);

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('successfully schedules a delivery, closes modal, and refreshes delivery data', async () => {
    const user = userEvent.setup();
    render(<DeliveryDashboard />);

    expect(await screen.findByRole('button', { name: /\+ Schedule Delivery/i })).toBeInTheDocument();
    expect(deliveryService.getExpectedDeliveries).toHaveBeenCalledTimes(1);
    expect(deliveryService.getDeliveryHistory).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole('button', { name: /\+ Schedule Delivery/i }));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();

    // Select PO
    const poSelect = await screen.findByLabelText(/Purchase Order/i);
    await user.selectOptions(poSelect, '103');

    // Enter Delivery Reference
    const refInput = screen.getByLabelText(/Delivery Reference/i);
    await user.type(refInput, 'DEL-2026-003');

    // Submit form
    const submitBtn = screen.getByRole('button', { name: 'Schedule Delivery' });
    await user.click(submitBtn);

    await waitFor(() => {
      expect(deliveryService.scheduleDelivery).toHaveBeenCalledWith(103, 'DEL-2026-003');
    });

    // Modal should close and data should be refreshed
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(deliveryService.getExpectedDeliveries).toHaveBeenCalledTimes(2);
      expect(deliveryService.getDeliveryHistory).toHaveBeenCalledTimes(2);
    });
  });
});

it('quality inspectors can read deliveries without scheduling or receiving controls', async () => {
  useAuth.mockReturnValue({ roles: ['QualityInspector'] });
  deliveryService.getExpectedDeliveries.mockResolvedValue([{ id: 1, deliveryReference: 'DEL-READ', status: 'Scheduled' }]);
  deliveryService.getDeliveryHistory.mockResolvedValue([]);
  render(<DeliveryDashboard />);
  await screen.findByText('DEL-READ');
  expect(screen.queryByRole('button', { name: /Schedule Delivery/ })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Receive', exact: true })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'AI Risk Check' })).not.toBeInTheDocument();
});
