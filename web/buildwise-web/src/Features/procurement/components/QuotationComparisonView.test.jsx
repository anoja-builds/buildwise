import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import QuotationComparisonView from './QuotationComparisonView'

const comparison = {
  materialRequestId: 101,
  projectName: 'Riverside Apartments — Block C',
  rows: [
    {
      materialRequestItemId: 1001,
      materialName: 'Cement (50kg bag)',
      unit: 'bag',
      requestedQuantity: 250,
      offers: [
        { quotationId: 501, supplierId: 1, supplierName: 'Supplier A', supplierStatus: 'Active', quantityOffered: 250, unitPrice: 2100, lineTotal: 525000, coversFullQuantity: true },
        { quotationId: 502, supplierId: 2, supplierName: 'Supplier B', supplierStatus: 'Suspended', quantityOffered: 250, unitPrice: 2040, lineTotal: 510000, coversFullQuantity: true },
        { quotationId: 503, supplierId: 3, supplierName: 'Supplier C', supplierStatus: 'Active', quantityOffered: 200, unitPrice: 2050, lineTotal: 410000, coversFullQuantity: false }
      ]
    }
  ],
  quotations: [
    { id: 501, supplierName: 'Supplier A', supplierStatus: 'Active', totalAmount: 525000, status: 'UnderReview' },
    { id: 502, supplierName: 'Supplier B', supplierStatus: 'Suspended', totalAmount: 510000, status: 'UnderReview' },
    { id: 503, supplierName: 'Supplier C', supplierStatus: 'Active', totalAmount: 410000, status: 'UnderReview' }
  ]
}

describe('QuotationComparisonView', () => {
  it('renders a per-supplier total and full coverage tag for a fully-covering quotation', () => {
    render(<QuotationComparisonView comparison={comparison} onRunAnalysis={() => {}} running={false} />)

    expect(screen.getByText('525,000')).toBeInTheDocument()
    expect(screen.getAllByText('Covers 250/250')).toHaveLength(2)
  })

  it('flags a partial-coverage quotation instead of treating it as a full match', () => {
    render(<QuotationComparisonView comparison={comparison} onRunAnalysis={() => {}} running={false} />)

    expect(screen.getByText('Partial 200/250')).toBeInTheDocument()
  })

  it('shows an empty state when there is nothing to compare yet', () => {
    render(<QuotationComparisonView comparison={{ rows: [], quotations: [] }} onRunAnalysis={() => {}} running={false} />)

    expect(screen.getByText('No quotations to compare yet')).toBeInTheDocument()
  })
})
