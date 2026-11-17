import { useEffect, useState } from 'react'
import { Alert, Badge, Button, Card, Form, Modal, Spinner, Table } from 'react-bootstrap'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { FiArrowDown, FiArrowLeft, FiArrowUp, FiChevronDown, FiChevronUp, FiEdit2, FiFilter, FiGrid, FiPaperclip, FiPlus, FiTrash2, FiUpload, FiX } from 'react-icons/fi'
import { api, apiError, downloadFile, uploadFile } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { date, dateTime, money, relative } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { AttachmentList, EmptyState, ExportMenu, Loading, PageHeader, Pager, PreviewModal, type FileInfo, useConfirm } from '../../components/ui'

export const FIELD_TYPES = [
  ['Text', 'Text'], ['LongText', 'Long text'], ['Number', 'Number'], ['Decimal', 'Decimal'], ['Currency', 'Currency (₹)'], ['Date', 'Date'], ['DateTime', 'Date & time'],
  ['Boolean', 'Yes / No'], ['Email', 'Email'], ['Phone', 'Phone'], ['Url', 'URL'], ['Dropdown', 'Dropdown'], ['MultiSelect', 'Multi-select'], ['FileAttachment', 'File attachment'], ['Image', 'Image'], ['Formula', 'Formula'],
] as const
interface Column { id?: string; name: string; fieldType: string; isRequired: boolean; isUnique: boolean; isVisible: boolean; defaultValue?: string; placeholder?: string; options?: string[]; minValue?: number | null; maxValue?: number | null; maxLength?: number | null; regexPattern?: string; formula?: string }
interface TableDef { id: string; name: string; description?: string; icon?: string; columns: Column[]; recordCount: number; updatedAt: string }
interface Rec { id: string; values: Record<string, string | null>; notes?: string; attachments: FileInfo[]; createdAt: string; updatedAt: string }

export default function TablesPage() {
  const { id } = useParams()
  return id ? <TableData id={id} /> : <TableList />
}

function TableList() {
  const nav = useNavigate()
  const [params, setParams] = useSearchParams()
  const list = useAsync(() => api.get('/api/tables').then((r) => r.data as { id: string; name: string; description?: string; columnCount: number; recordCount: number; updatedAt: string }[]), [])
  const [builder, setBuilder] = useState(false)
  useEffect(() => { if (params.get('new')) { setBuilder(true); params.delete('new'); setParams(params, { replace: true }) } }, [params, setParams])
  return (
    <>
      <PageHeader title="Custom Tables" subtitle="Build your own registers — no programming needed" icon={<FiGrid />}><Button onClick={() => setBuilder(true)}><FiPlus /> Create new table</Button></PageHeader>
      {list.loading ? <Loading /> : list.data?.length === 0 ? <Card><EmptyState icon={<FiGrid />} title="No tables yet" text="Create a Medicine Register, Asset list, Customer records — anything you need." action={<Button onClick={() => setBuilder(true)}><FiPlus /> Create new table</Button>} /></Card> : (
        <div className="row g-3">{list.data?.map((t) => (
          <div className="col-md-6 col-xl-4" key={t.id}><Card className="h-100 clickable" onClick={() => nav(`/tables/${t.id}`)}><Card.Body>
            <div className="d-flex gap-3"><span className="stat-card p-0"><span className="icon badge-soft"><FiGrid /></span></span>
              <div className="min-w-0"><h6 className="fw-bold mb-1 truncate">{t.name}</h6><div className="small text-muted text-truncate">{t.description || 'No description'}</div>
                <div className="small mt-2"><Badge bg="" className="badge-soft me-1">{t.columnCount} columns</Badge><Badge bg="" className="bg-soft-success">{t.recordCount} records</Badge></div>
                <div className="small text-muted mt-1">Updated {relative(t.updatedAt)}</div></div></div>
          </Card.Body></Card></div>))}</div>)}
      {builder && <TableBuilder onClose={(newId) => { setBuilder(false); if (newId) nav(`/tables/${newId}`); else void list.reload() }} />}
    </>
  )
}

const blankColumn = (): Column => ({ name: '', fieldType: 'Text', isRequired: false, isUnique: false, isVisible: true, options: [] })

/** Step 1: name & description · Step 2–4: columns, types and settings · Step 5: save. Also used for editing a definition. */
export function TableBuilder({ table, onClose }: { table?: TableDef; onClose: (newId?: string) => void }) {
  const toast = useToast()
  const [step, setStep] = useState(table ? 2 : 1)
  const [name, setName] = useState(table?.name ?? '')
  const [description, setDescription] = useState(table?.description ?? '')
  const [cols, setCols] = useState<Column[]>(table?.columns.map((c) => ({ ...c })) ?? [blankColumn()])
  const [expanded, setExpanded] = useState<number | null>(table ? null : 0)
  const [impacts, setImpacts] = useState<string[] | null>(null)
  const [busy, setBusy] = useState(false)
  const set = (i: number, patch: Partial<Column>) => setCols(cols.map((c, j) => (j === i ? { ...c, ...patch } : c)))
  const move = (i: number, d: number) => { const n = [...cols]; const [x] = n.splice(i, 1); n.splice(i + d, 0, x!); setCols(n) }
  const numeric = cols.filter((c) => ['Number', 'Decimal', 'Currency'].includes(c.fieldType) && c.name).map((c) => c.name)
  const save = async (confirmDataLoss = false) => {
    setBusy(true)
    const body = { name, description, columns: cols.map((c) => ({ ...c, minValue: c.minValue ?? null, maxValue: c.maxValue ?? null, maxLength: c.maxLength || null })), confirmDataLoss }
    try {
      if (table) { await api.put(`/api/tables/${table.id}`, body); toast.success('Table updated'); onClose() }
      else { const r = await api.post('/api/tables', body); toast.success('Table created'); onClose(r.data.id) }
    } catch (x) {
      const e = apiError(x)
      if (e.fields.confirmDataLoss) setImpacts((x as { response: { data: { errors: { confirmDataLoss: string[] } } } }).response.data.errors.confirmDataLoss)
      else toast.push('error', e.message)
    } finally { setBusy(false) }
  }
  return (
    <Modal show onHide={() => onClose()} size="xl" centered backdrop="static">
      <Modal.Header closeButton><Modal.Title className="h5">{table ? `Edit “${table.name}”` : 'Create new table'}</Modal.Title></Modal.Header>
      <Modal.Body>
        <div className="d-flex gap-2 mb-3 small">{['Name', 'Columns & settings', 'Review'].map((s, i) => <Badge key={s} bg="" className={step === i + 1 ? 'bg-primary' : 'badge-soft'}>{i + 1}. {s}</Badge>)}</div>
        {step === 1 && (
          <div className="row g-3">
            <Form.Group className="col-md-6"><Form.Label>Table name</Form.Label><Form.Control autoFocus value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Medicine Register" /></Form.Group>
            <Form.Group className="col-md-6"><Form.Label>Description</Form.Label><Form.Control value={description} onChange={(e) => setDescription(e.target.value)} placeholder="What will you keep here?" /></Form.Group>
          </div>)}
        {step === 2 && (
          <>
            {table && <Form.Control className="mb-3" value={name} onChange={(e) => setName(e.target.value)} aria-label="Table name" />}
            {cols.map((c, i) => (
              <Card key={i} className="mb-2"><Card.Body className="py-2">
                <div className="row g-2 align-items-center">
                  <div className="col-md-5"><Form.Control size="sm" placeholder={`Column ${i + 1} name`} value={c.name} onChange={(e) => set(i, { name: e.target.value })} /></div>
                  <div className="col-md-3"><Form.Select size="sm" value={c.fieldType} onChange={(e) => set(i, { fieldType: e.target.value })}>{FIELD_TYPES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</Form.Select></div>
                  <div className="col-md-4 d-flex gap-1 justify-content-end align-items-center">
                    {c.isRequired && <Badge bg="" className="bg-soft-danger">Required</Badge>}{c.isUnique && <Badge bg="" className="bg-soft-info">Unique</Badge>}
                    <Button size="sm" variant="link" onClick={() => setExpanded(expanded === i ? null : i)}>Settings {expanded === i ? <FiChevronUp /> : <FiChevronDown />}</Button>
                    <Button size="sm" variant="link" disabled={i === 0} onClick={() => move(i, -1)}><FiArrowUp /></Button>
                    <Button size="sm" variant="link" disabled={i === cols.length - 1} onClick={() => move(i, 1)}><FiArrowDown /></Button>
                    <Button size="sm" variant="link" className="text-danger" disabled={cols.length === 1} onClick={() => setCols(cols.filter((_, j) => j !== i))}><FiTrash2 /></Button>
                  </div>
                </div>
                {expanded === i && (
                  <div className="row g-2 mt-1 small">
                    <div className="col-12 d-flex flex-wrap gap-3">
                      <Form.Check id={`rq${i}`} label="Required" checked={c.isRequired} disabled={c.fieldType === 'Formula'} onChange={(e) => set(i, { isRequired: e.target.checked })} />
                      <Form.Check id={`un${i}`} label="Unique" checked={c.isUnique} disabled={['Boolean', 'Formula', 'FileAttachment', 'Image', 'MultiSelect'].includes(c.fieldType)} onChange={(e) => set(i, { isUnique: e.target.checked })} />
                      <Form.Check id={`vi${i}`} label="Visible in list" checked={c.isVisible} onChange={(e) => set(i, { isVisible: e.target.checked })} />
                    </div>
                    {!['Formula', 'FileAttachment', 'Image', 'Boolean'].includes(c.fieldType) && <>
                      <div className="col-md-4"><Form.Control size="sm" placeholder="Default value" value={c.defaultValue ?? ''} onChange={(e) => set(i, { defaultValue: e.target.value })} /></div>
                      <div className="col-md-4"><Form.Control size="sm" placeholder="Placeholder text" value={c.placeholder ?? ''} onChange={(e) => set(i, { placeholder: e.target.value })} /></div></>}
                    {['Number', 'Decimal', 'Currency'].includes(c.fieldType) && <>
                      <div className="col-md-2"><Form.Control size="sm" type="number" placeholder="Min" value={c.minValue ?? ''} onChange={(e) => set(i, { minValue: e.target.value === '' ? null : Number(e.target.value) })} /></div>
                      <div className="col-md-2"><Form.Control size="sm" type="number" placeholder="Max" value={c.maxValue ?? ''} onChange={(e) => set(i, { maxValue: e.target.value === '' ? null : Number(e.target.value) })} /></div></>}
                    {['Text', 'LongText'].includes(c.fieldType) && <>
                      <div className="col-md-2"><Form.Control size="sm" type="number" placeholder="Max length" value={c.maxLength ?? ''} onChange={(e) => set(i, { maxLength: e.target.value ? Number(e.target.value) : null })} /></div>
                      <div className="col-md-2"><Form.Control size="sm" placeholder="Pattern (regex)" value={c.regexPattern ?? ''} onChange={(e) => set(i, { regexPattern: e.target.value })} /></div></>}
                    {['Dropdown', 'MultiSelect'].includes(c.fieldType) && <div className="col-12"><Form.Control size="sm" placeholder="Options, comma separated (e.g. Tablet, Syrup, Injection)" value={(c.options ?? []).join(', ')} onChange={(e) => set(i, { options: e.target.value.split(',').map((o) => o.trim()) })} /></div>}
                    {c.fieldType === 'Formula' && <div className="col-12"><Form.Control size="sm" placeholder="e.g. {MRP} * {GST} / 100" value={c.formula ?? ''} onChange={(e) => set(i, { formula: e.target.value })} />
                      <div className="text-muted mt-1">Use number columns in braces: {numeric.length ? numeric.map((n) => <code key={n} className="me-1">{`{${n}}`}</code>) : 'add a Number/Decimal/Currency column first'}. Operators: + − × ÷ % ( )</div></div>}
                  </div>)}
              </Card.Body></Card>))}
            <Button variant="outline-primary" size="sm" onClick={() => { setCols([...cols, blankColumn()]); setExpanded(cols.length) }}><FiPlus /> Add column</Button>
          </>)}
        {step === 3 && (
          <>
            <h6 className="fw-bold">{name}</h6><p className="small text-muted">{description}</p>
            <Table size="sm" className="small"><thead><tr><th>#</th><th>Column</th><th>Type</th><th>Rules</th></tr></thead>
              <tbody>{cols.map((c, i) => <tr key={i}><td>{i + 1}</td><td>{c.name}</td><td>{FIELD_TYPES.find((f) => f[0] === c.fieldType)?.[1]}</td>
                <td>{[c.isRequired && 'required', c.isUnique && 'unique', !c.isVisible && 'hidden', c.formula && `= ${c.formula}`, c.options?.filter(Boolean).length && `${c.options.filter(Boolean).length} options`].filter(Boolean).join(' · ')}</td></tr>)}</tbody></Table>
            {impacts && <Alert variant="danger"><strong>These changes affect existing data:</strong><ul className="mb-2">{impacts.map((x) => <li key={x}>{x}</li>)}</ul><Button variant="danger" size="sm" onClick={() => void save(true)}>I understand — apply changes</Button></Alert>}
          </>)}
      </Modal.Body>
      <Modal.Footer>
        {step > 1 && <Button variant="outline-secondary" onClick={() => setStep(step - 1)}>Back</Button>}
        {step < 3 ? <Button disabled={!name.trim() || (step === 2 && cols.some((c) => !c.name.trim()))} onClick={() => setStep(step + 1)}>Next</Button>
          : <Button disabled={busy} onClick={() => void save()}>{busy ? <Spinner size="sm" /> : table ? 'Save changes' : 'Create table'}</Button>}
      </Modal.Footer>
    </Modal>
  )
}

function display(c: Column, v: string | null | undefined) {
  if (v === null || v === undefined || v === '') return <span className="text-muted">—</span>
  switch (c.fieldType) {
    case 'Currency': return money(Number(v))
    case 'Date': return date(v)
    case 'DateTime': return dateTime(v)
    case 'Boolean': return v === 'true' ? <Badge bg="" className="bg-soft-success">Yes</Badge> : <Badge bg="" className="badge-soft">No</Badge>
    case 'Url': return <a href={v} target="_blank" rel="noreferrer noopener" onClick={(e) => e.stopPropagation()}>{v.replace(/^https?:\/\//, '')}</a>
    case 'Email': return <a href={`mailto:${v}`} onClick={(e) => e.stopPropagation()}>{v}</a>
    case 'MultiSelect': return (JSON.parse(v) as string[]).map((o) => <span className="tag" key={o}>{o}</span>)
    case 'FileAttachment': case 'Image': return <FiPaperclip />
    case 'LongText': return v.length > 60 ? v.slice(0, 60) + '…' : v
    default: return v
  }
}

function TableData({ id }: { id: string }) {
  const nav = useNavigate()
  const toast = useToast()
  const confirm = useConfirm()
  const [params, setParams] = useSearchParams()
  const def = useAsync(() => api.get<TableDef>(`/api/tables/${id}`).then((r) => r.data), [id])
  const [q, setQ] = useState('')
  const search = useDebounced(q)
  const [sort, setSort] = useState<{ col?: string; desc: boolean }>({ desc: false })
  const [filters, setFilters] = useState<{ columnId: string; op: string; value: string }[]>([])
  const [page, setPage] = useState(1)
  const recs = useAsync(() => api.post(`/api/tables/${id}/records/query`, { search: search || null, sortColumnId: sort.col ?? null, sortDesc: sort.desc, filters: filters.filter((f) => f.columnId && (f.value || f.op.includes('empty'))), page, pageSize: 25 })
    .then((r) => r.data as { items: Rec[]; totalPages: number; totalCount: number }), [id, search, sort.col, sort.desc, JSON.stringify(filters), page])
  const [edit, setEdit] = useState<Partial<Rec> | null>(null)
  const [builder, setBuilder] = useState(false)
  const [showFilters, setShowFilters] = useState(false)
  const [importRes, setImportRes] = useState<{ imported: number; failed: number; errors: string[] } | null>(null)
  useEffect(() => {
    const open = params.get('open')
    if (open) { api.get<Rec>(`/api/tables/${id}/records/${open}`).then((r) => setEdit(r.data)).catch(() => {}); params.delete('open'); setParams(params, { replace: true }) }
  }, [params, setParams, id])
  if (def.loading) return <Loading />
  if (!def.data) return <Alert variant="danger">Table not found.</Alert>
  const t = def.data
  const visible = t.columns.filter((c) => c.isVisible)
  const doImport = async (f?: File) => {
    if (!f) return
    try { setImportRes(await uploadFile(`/api/tables/${id}/import`, f)); void recs.reload() } catch (e) { toast.error(e) }
  }
  return (
    <>
      <PageHeader title={t.name} subtitle={t.description} icon={<FiGrid />}>
        <Button variant="outline-secondary" size="sm" onClick={() => nav('/tables')}><FiArrowLeft /> Tables</Button>
        <Button variant="outline-secondary" size="sm" onClick={() => setBuilder(true)}><FiEdit2 /> Edit columns</Button>
        <label className="btn btn-sm btn-outline-secondary mb-0"><FiUpload /> Import<input type="file" hidden accept=".csv,.xlsx" onChange={(e) => { void doImport(e.target.files?.[0]); e.target.value = '' }} /></label>
        <ExportMenu url={(f) => `/api/tables/${id}/export?format=${f}`} />
        <Button size="sm" onClick={() => setEdit({ values: {} })}><FiPlus /> Add record</Button>
      </PageHeader>
      <Card className="mb-3"><Card.Body className="d-flex flex-wrap gap-2">
        <Form.Control placeholder="Search records…" value={q} onChange={(e) => { setQ(e.target.value); setPage(1) }} style={{ maxWidth: 340 }} />
        <Button variant={filters.length ? 'primary' : 'outline-secondary'} size="sm" onClick={() => { setShowFilters(!showFilters); if (!filters.length) setFilters([{ columnId: t.columns[0]?.id ?? '', op: 'contains', value: '' }]) }}><FiFilter /> Filters {filters.length > 0 && `(${filters.length})`}</Button>
        <Button variant="outline-danger" size="sm" className="ms-auto" onClick={async () => { if (await confirm({ title: `Delete table “${t.name}”?`, body: 'All records in this table will be deleted.' })) { await api.delete(`/api/tables/${id}`); nav('/tables') } }}><FiTrash2 /> Delete table</Button>
        {showFilters && <div className="w-100">{filters.map((f, i) => {
          const col = t.columns.find((c) => c.id === f.columnId)
          const numeric = col && ['Number', 'Decimal', 'Currency', 'Date', 'DateTime'].includes(col.fieldType)
          return (<div key={i} className="d-flex gap-2 mb-2">
            <Form.Select size="sm" value={f.columnId} onChange={(e) => setFilters(filters.map((x, j) => (j === i ? { ...x, columnId: e.target.value } : x)))} style={{ maxWidth: 200 }}>{t.columns.filter((c) => c.fieldType !== 'Formula').map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</Form.Select>
            <Form.Select size="sm" value={f.op} onChange={(e) => setFilters(filters.map((x, j) => (j === i ? { ...x, op: e.target.value } : x)))} style={{ maxWidth: 150 }}>
              {(numeric ? [['eq', '='], ['gt', '>'], ['gte', '≥'], ['lt', '<'], ['lte', '≤']] : [['contains', 'contains'], ['eq', 'equals']]).concat([['empty', 'is empty'], ['notempty', 'is not empty']]).map(([v, l]) => <option key={v} value={v}>{l}</option>)}</Form.Select>
            {!f.op.includes('empty') && <Form.Control size="sm" type={col?.fieldType.startsWith('Date') ? 'date' : 'text'} value={f.value} onChange={(e) => setFilters(filters.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)))} style={{ maxWidth: 220 }} />}
            <Button size="sm" variant="link" className="text-danger" onClick={() => setFilters(filters.filter((_, j) => j !== i))}><FiX /></Button>
          </div>)
        })}<Button size="sm" variant="link" onClick={() => setFilters([...filters, { columnId: t.columns[0]?.id ?? '', op: 'contains', value: '' }])}><FiPlus /> Add filter</Button></div>}
      </Card.Body></Card>
      <Card>
        {recs.loading && !recs.data ? <Loading /> : recs.data?.items.length === 0 ? <EmptyState title="No records" text="Add your first record or import from Excel/CSV (column headers must match column names)." action={<Button onClick={() => setEdit({ values: {} })}><FiPlus /> Add record</Button>} /> : (
          <Table responsive hover className="mb-0 align-middle small">
            <thead><tr>{visible.map((c) => <th key={c.id} className="clickable" onClick={() => c.fieldType !== 'Formula' && setSort({ col: c.id, desc: sort.col === c.id ? !sort.desc : false })}>{c.name} {sort.col === c.id && (sort.desc ? <FiArrowDown /> : <FiArrowUp />)}</th>)}<th /></tr></thead>
            <tbody>{recs.data?.items.map((r) => (
              <tr key={r.id} className="clickable" onClick={() => setEdit(r)}>
                {visible.map((c) => <td key={c.id}>{display(c, r.values[c.id!])}</td>)}
                <td className="text-end text-nowrap">{r.attachments.length > 0 && <Badge bg="" className="badge-soft me-1"><FiPaperclip /> {r.attachments.length}</Badge>}
                  <Button size="sm" variant="link" className="text-danger" onClick={async (e) => { e.stopPropagation(); if (await confirm({ title: 'Delete this record?' })) { await api.delete(`/api/tables/${id}/records/${r.id}`); void recs.reload() } }}><FiTrash2 /></Button></td>
              </tr>))}</tbody>
          </Table>)}
        <Card.Footer className="d-flex justify-content-between align-items-center"><small className="text-muted">{recs.data?.totalCount ?? 0} record(s)</small><Pager page={page} totalPages={recs.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer>
      </Card>
      {edit && <RecordModal table={t} record={edit} onClose={(ok) => { setEdit(null); if (ok) void recs.reload() }} />}
      {builder && <TableBuilder table={t} onClose={() => { setBuilder(false); void def.reload(); void recs.reload() }} />}
      <Modal show={!!importRes} onHide={() => setImportRes(null)} centered>
        <Modal.Header closeButton><Modal.Title className="h5">Import result</Modal.Title></Modal.Header>
        <Modal.Body><p><Badge bg="success">{importRes?.imported} imported</Badge> {importRes?.failed ? <Badge bg="danger">{importRes.failed} failed</Badge> : null}</p>
          {importRes?.errors.map((e) => <div key={e} className="small text-danger">{e}</div>)}</Modal.Body>
      </Modal>
    </>
  )
}

function FieldInput({ col, value, onChange, tableId }: { col: Column; value: string; onChange: (v: string) => void; tableId: string }) {
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const common = { placeholder: col.placeholder ?? '', value, onChange: (e: React.ChangeEvent<HTMLInputElement>) => onChange(e.target.value) }
  switch (col.fieldType) {
    case 'LongText': return <Form.Control as="textarea" rows={3} {...common} />
    case 'Number': return <Form.Control type="number" step="1" {...common} />
    case 'Decimal': case 'Currency': return <Form.Control type="number" step="any" {...common} />
    case 'Date': return <Form.Control type="date" {...common} />
    case 'DateTime': return <Form.Control type="datetime-local" {...common} value={value ? value.slice(0, 16) : ''} />
    case 'Email': return <Form.Control type="email" {...common} />
    case 'Phone': return <Form.Control type="tel" {...common} />
    case 'Url': return <Form.Control type="url" {...common} />
    case 'Boolean': return <Form.Check type="switch" id={col.id} checked={value === 'true'} onChange={(e) => onChange(e.target.checked ? 'true' : 'false')} label={value === 'true' ? 'Yes' : 'No'} />
    case 'Dropdown': return <Form.Select value={value} onChange={(e) => onChange(e.target.value)}><option value="">—</option>{col.options?.map((o) => <option key={o}>{o}</option>)}</Form.Select>
    case 'MultiSelect': {
      const sel: string[] = value ? JSON.parse(value) : []
      return <div>{col.options?.map((o) => <Form.Check inline key={o} id={`${col.id}-${o}`} label={o} checked={sel.includes(o)} onChange={(e) => onChange(JSON.stringify(e.target.checked ? [...sel, o] : sel.filter((s) => s !== o)))} />)}</div>
    }
    case 'FileAttachment': case 'Image':
      return (<div className="d-flex gap-2 align-items-center">
        <Form.Control type="file" accept={col.fieldType === 'Image' ? 'image/*' : undefined} onChange={async (e) => {
          const f = (e.target as HTMLInputElement).files?.[0]; if (!f) return
          setBusy(true)
          try { const r = await uploadFile<{ id: string }>(`/api/tables/${tableId}/files?image=${col.fieldType === 'Image'}`, f); onChange(r.id) } catch (x) { toast.error(x) } finally { setBusy(false) }
        }} />
        {busy && <Spinner size="sm" />}{value && !busy && <><Badge bg="" className="bg-soft-success">Attached</Badge><Button size="sm" variant="link" onClick={() => void downloadFile(`/api/files/${value}`)}>Open</Button><Button size="sm" variant="link" className="text-danger" onClick={() => onChange('')}>Remove</Button></>}
      </div>)
    case 'Formula': return <Form.Control readOnly value={value || 'Calculated on save'} className="bg-body-tertiary" />
    default: return <Form.Control {...common} maxLength={col.maxLength ?? undefined} />
  }
}

function RecordModal({ table, record, onClose }: { table: TableDef; record: Partial<Rec>; onClose: (ok: boolean) => void }) {
  const toast = useToast()
  const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(table.columns.map((c) => [c.id!, record.values?.[c.id!] ?? ''])))
  const [notes, setNotes] = useState(record.notes ?? '')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [atts, setAtts] = useState<FileInfo[]>(record.attachments ?? [])
  const [preview, setPreview] = useState<FileInfo | null>(null)
  const save = async () => {
    setBusy(true); setErrors({})
    try {
      const body = { values: Object.fromEntries(Object.entries(values).map(([k, v]) => [k, v === '' ? null : v])), notes }
      if (record.id) await api.put(`/api/tables/${table.id}/records/${record.id}`, body); else await api.post(`/api/tables/${table.id}/records`, body)
      toast.success('Record saved'); onClose(true)
    } catch (x) { const e = apiError(x); setErrors(e.fields); toast.push('error', Object.keys(e.fields).length ? 'Please fix the highlighted fields.' : e.message) } finally { setBusy(false) }
  }
  return (
    <Modal show onHide={() => onClose(false)} size="lg" centered>
      <Modal.Header closeButton><Modal.Title className="h5">{record.id ? 'Edit record' : 'Add record'}</Modal.Title></Modal.Header>
      <Modal.Body className="row g-3">
        {table.columns.map((c) => (
          <Form.Group key={c.id} className={c.fieldType === 'LongText' || c.fieldType === 'MultiSelect' ? 'col-12' : 'col-md-6'}>
            <Form.Label className="small fw-semibold">{c.name}{c.isRequired && <span className="text-danger"> *</span>}</Form.Label>
            <FieldInput col={c} value={values[c.id!] ?? ''} onChange={(v) => setValues({ ...values, [c.id!]: v })} tableId={table.id} />
            {errors[c.id!] && <div className="small text-danger">{errors[c.id!]}</div>}
          </Form.Group>))}
        <Form.Group className="col-12"><Form.Label className="small fw-semibold">Notes</Form.Label><Form.Control as="textarea" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} /></Form.Group>
        {record.id && <div className="col-12">
          <div className="d-flex justify-content-between align-items-center"><Form.Label className="small fw-semibold mb-0">Attachments (PDF, images…)</Form.Label>
            <label className="btn btn-sm btn-outline-primary mb-0"><FiPaperclip /> Attach<input type="file" hidden onChange={async (e) => { const f = e.target.files?.[0]; if (!f) return; try { const r = await uploadFile<FileInfo>(`/api/tables/${table.id}/records/${record.id}/attachments`, f); setAtts([...atts, r]) } catch (x) { toast.error(x) } }} /></label></div>
          <AttachmentList files={atts} urlFor={(f) => `/api/files/${f.id}`} onPreview={setPreview} onRemove={async (f) => { await api.delete(`/api/tables/${table.id}/records/${record.id}/attachments/${f.id}`); setAtts(atts.filter((a) => a.id !== f.id)) }} />
        </div>}
        {!record.id && <div className="col-12 small text-muted">You can add file attachments after saving the record.</div>}
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button disabled={busy} onClick={() => void save()}>{busy ? <Spinner size="sm" /> : 'Save'}</Button></Modal.Footer>
      <PreviewModal url={preview ? `/api/files/${preview.id}?inline=true` : null} name={preview?.name} contentType={preview?.contentType} onHide={() => setPreview(null)} />
    </Modal>
  )
}
