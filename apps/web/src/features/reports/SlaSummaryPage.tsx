import { useState } from 'react';
import { EmptyState } from '../../components/EmptyState';
import { ErrorMessage } from '../../components/ErrorMessage';
import { Field } from '../../components/Field';
import { Spinner } from '../../components/Spinner';
import type { SlaSummaryGroup, SlaSummaryQuery, SlaSummaryGrouping } from '../../types/api';
import { defaultReportSelection, validateReportSelection } from './reportDates';
import type { ReportErrors, ReportSelection } from './reportDates';
import { useSlaSummary } from './useSlaSummary';

function SummaryTable({ rows, grouping }: { rows: SlaSummaryGroup[]; grouping: SlaSummaryGrouping }) {
  return <div className="table-wrapper" role="region" aria-label="SLA summary results" tabIndex={0}>
    <table className="table sla-report-table">
      <caption className="visually-hidden">SLA summary grouped by {grouping}</caption>
      <thead><tr>
        <th scope="col">{grouping === 'agent' ? 'Agent' : 'Priority'}</th>
        <th scope="col">Total</th><th scope="col">Breached</th><th scope="col">At risk</th>
        <th scope="col">Within SLA</th><th scope="col">Met</th><th scope="col">No SLA</th>
        <th scope="col">Breach rate</th><th scope="col">Avg. resolution (hours)</th>
      </tr></thead>
      <tbody>{rows.map((row) => <tr key={grouping === 'agent' ? row.agentId ?? 'unassigned' : row.priority}>
        <th scope="row">{grouping === 'agent' ? row.agentName ?? 'Unassigned' : row.priority}</th>
        <td>{row.total}</td><td>{row.breached}</td><td>{row.atRisk}</td><td>{row.within}</td>
        <td>{row.met}</td><td>{row.notApplicable}</td><td>{row.breachRate.toFixed(2)}%</td>
        <td>{row.averageResolutionHours === null ? 'No resolutions' : row.averageResolutionHours.toFixed(2)}</td>
      </tr>)}</tbody>
    </table>
  </div>;
}

export function SlaSummaryPage() {
  const [selection, setSelection] = useState(defaultReportSelection);
  const [errors, setErrors] = useState<ReportErrors>({});
  const [applied, setApplied] = useState<{ selection: ReportSelection; query: SlaSummaryQuery }>();
  const report = useSlaSummary(applied?.query);
  function generate() {
    const validation = validateReportSelection(selection);
    setErrors(validation.errors ?? {});
    if (validation.query) setApplied({ selection: { ...selection }, query: validation.query });
  }

  return <section>
    <header className="page-header"><div>
      <h1>SLA summary</h1>
      <p className="page-header__subtitle">Current SLA outcomes for tickets created during a selected UTC date range.</p>
    </div></header>
    <form className="filters" aria-label="SLA summary filters" noValidate onSubmit={(event) => { event.preventDefault(); generate(); }}>
      <fieldset className="report-controls" disabled={report.isLoading}>
        <legend className="visually-hidden">Report filters</legend>
        <div className="filters__row">
          <Field id="report-from" label="From date (UTC)" error={errors.from}>
            {(props) => <input {...props} type="date" required value={selection.from}
              onChange={(event) => setSelection((current) => ({ ...current, from: event.target.value }))} />}
          </Field>
          <Field id="report-to" label="To date (UTC)" error={errors.to}>
            {(props) => <input {...props} type="date" required value={selection.to}
              onChange={(event) => setSelection((current) => ({ ...current, to: event.target.value }))} />}
          </Field>
          <Field id="report-group" label="Group by">
            {(props) => <select {...props} value={selection.groupBy} onChange={(event) => {
              const groupBy = event.target.value;
              if (groupBy === 'agent' || groupBy === 'priority') setSelection((current) => ({ ...current, groupBy }));
            }}><option value="agent">Agent</option><option value="priority">Priority</option></select>}
          </Field>
        </div>
        <p className="field__hint">Both selected dates are included. Breach rate is breached tickets divided by all tickets, including No SLA.</p>
        <div className="filters__row filters__row--actions">
          <button type="submit" className="button button--primary">{report.isLoading ? 'Generating...' : 'Generate report'}</button>
        </div>
      </fieldset>
    </form>
    {!applied && <EmptyState title="Generate an SLA summary" description="Choose dates and a grouping, then generate the report." />}
    {report.isLoading && <Spinner label="Loading SLA summary" />}
    {report.error && <ErrorMessage message={report.error} onRetry={report.reload} />}
    {applied && report.data && <>
      <p className="page-header__subtitle">Showing tickets created {applied.selection.from} through {applied.selection.to} UTC, grouped by {applied.selection.groupBy}.</p>
      <p className="field__hint">Average resolution measures creation to resolution for resolved tickets only; No SLA resolutions are included. This is a current-state report.</p>
      {report.data.length === 0 ? <EmptyState title="No tickets in this date range" description="Try a different creation date range." /> : <>
        <p className="field__hint">On smaller screens, scroll the table horizontally to see all metrics.</p>
        <SummaryTable rows={report.data} grouping={applied.selection.groupBy} />
      </>}
    </>}
  </section>;
}
