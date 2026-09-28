import { describe, it, expect, vi, beforeEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import DeliveryDiscrepancyPanel from '../components/DeliveryDiscrepancyPanel';
import { deliveryService } from '../services/deliveryService';

// Mock the delivery service
vi.mock('../services/deliveryService', () => ({
  deliveryService: {
    analyzeDiscrepancies: vi.fn(),
    getDiscrepancyHistory: vi.fn(),
  }
}));

describe('DeliveryDiscrepancyPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    deliveryService.getDiscrepancyHistory.mockResolvedValue([]);
  });

  it('renders initial state with Run button', () => {
    render(<DeliveryDiscrepancyPanel deliveryId={1} />);
    expect(screen.getByText(/Delivery Discrepancy Agent/)).toBeTruthy();
    expect(screen.getByText('Run Discrepancy Analysis')).toBeTruthy();
  });

  it('shows loading state when analysis is running', async () => {
    deliveryService.analyzeDiscrepancies.mockImplementation(
      () => new Promise(() => {}) // Never resolves
    );

    render(<DeliveryDiscrepancyPanel deliveryId={1} />);
    fireEvent.click(screen.getByText('Run Discrepancy Analysis'));

    expect(screen.getByText(/Agent comparing ordered/)).toBeTruthy();
  });

  it('displays cement scenario results correctly', async () => {
    const cementResult = {
      workflowId: 42,
      deliveryId: 1,
      purchaseOrderId: 10,
      supplierName: 'Lanka Cement Ltd',
      hasDiscrepancies: true,
      totalShortage: 10,
      totalDamaged: 5,
      totalUndamagedReceived: 235,
      executionMode: 'Deterministic',
      items: [{
        materialName: 'Cement',
        materialUnit: 'bags',
        orderedQuantity: 250,
        previouslyReceivedQuantity: 0,
        newlyReceivedQuantity: 240,
        damagedQuantity: 5,
        undamagedReceivedQuantity: 235,
        shortageQuantity: 10,
        hasDiscrepancy: true,
        discrepancyFlags: ['Shortage', 'Damage']
      }],
      recommendations: [
        {
          category: 'Shortage',
          materialName: 'Cement',
          severity: 'Medium',
          description: 'Shortage of 10 bags detected.',
          advisory: 'Notify procurement team.',
          isActionRequired: true
        },
        {
          category: 'Damage',
          materialName: 'Cement',
          severity: 'Medium',
          description: '5 bags arrived damaged.',
          advisory: 'Document with photographic evidence.',
          isActionRequired: true
        }
      ],
      validation: { isValid: true, errors: [] }
    };

    deliveryService.analyzeDiscrepancies.mockResolvedValue(cementResult);

    render(<DeliveryDiscrepancyPanel deliveryId={1} />);
    fireEvent.click(screen.getByText('Run Discrepancy Analysis'));

    await waitFor(() => {
      expect(screen.getByText('DISCREPANCIES FOUND')).toBeTruthy();
    });

    // Verify quantities displayed
    expect(screen.getByText('10')).toBeTruthy();  // shortage
    expect(screen.getByText('5')).toBeTruthy();   // damaged
    expect(screen.getByText('235')).toBeTruthy(); // undamaged received
    expect(screen.getByText('Cement')).toBeTruthy();
    expect(screen.getByText('Shortage')).toBeTruthy();
    expect(screen.getByText('Damage')).toBeTruthy();
  });

  it('displays error when API fails', async () => {
    deliveryService.analyzeDiscrepancies.mockRejectedValue(
      new Error('Delivery has not been received yet.')
    );

    render(<DeliveryDiscrepancyPanel deliveryId={1} />);
    fireEvent.click(screen.getByText('Run Discrepancy Analysis'));

    await waitFor(() => {
      expect(screen.getByText(/Delivery has not been received yet/)).toBeTruthy();
    });
  });

  it('does not show mock data on API failure', async () => {
    deliveryService.analyzeDiscrepancies.mockRejectedValue(
      new Error('Server error')
    );

    render(<DeliveryDiscrepancyPanel deliveryId={1} />);
    fireEvent.click(screen.getByText('Run Discrepancy Analysis'));

    await waitFor(() => {
      expect(screen.getByText(/Server error/)).toBeTruthy();
    });

    // No analysis data should be shown
    expect(screen.queryByText('DISCREPANCIES FOUND')).toBeNull();
    expect(screen.queryByText('NO DISCREPANCIES')).toBeNull();
  });

  it('displays workflow history when available', async () => {
    deliveryService.getDiscrepancyHistory.mockResolvedValue([
      {
        workflowId: 10,
        status: 'Completed',
        finalOutcome: 'Discrepancies found: Shortage=10, Damaged=5',
        completedAt: '2026-09-28T03:00:00Z',
        steps: [{ stepName: 'Data Retrieval' }, { stepName: 'Analysis' }]
      }
    ]);

    render(<DeliveryDiscrepancyPanel deliveryId={1} />);

    await waitFor(() => {
      expect(screen.getByText('History (1)')).toBeTruthy();
    });
  });

  it('calls API with correct delivery ID', async () => {
    deliveryService.analyzeDiscrepancies.mockResolvedValue({
      hasDiscrepancies: false,
      items: [],
      recommendations: [],
      validation: { isValid: true, errors: [] },
      totalShortage: 0,
      totalDamaged: 0,
      totalUndamagedReceived: 100,
      workflowId: 1,
      executionMode: 'Deterministic'
    });

    render(<DeliveryDiscrepancyPanel deliveryId={42} />);
    fireEvent.click(screen.getByText('Run Discrepancy Analysis'));

    await waitFor(() => {
      expect(deliveryService.analyzeDiscrepancies).toHaveBeenCalledWith(42);
    });
  });
});

describe('deliveryService', () => {
  it('exports analyzeDiscrepancies and getDiscrepancyHistory methods', () => {
    expect(typeof deliveryService.analyzeDiscrepancies).toBe('function');
    expect(typeof deliveryService.getDiscrepancyHistory).toBe('function');
  });
});
