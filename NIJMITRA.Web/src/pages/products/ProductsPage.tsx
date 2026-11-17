import { useEffect, useState } from 'react'
import { Alert, Badge, Button, Card, Form, Modal, Nav, Spinner, Table } from 'react-bootstrap'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { FiArrowLeft, FiEdit2, FiPackage, FiPaperclip, FiPlus, FiTrash2, FiUpload } from 'react-icons/fi'
import { api, uploadFile } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { date, money } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { Avatar, AttachmentList, EmptyState, ExportMenu, Loading, PageHeader, Pager, PreviewModal, type FileInfo, useConfirm } from '../../components/ui'

interface Field { id?: string; name: string; fieldType: string }
interface Register { id: string; name: string; description?: string; customFields: Field[]; productCount: number }
interface Product { id: string; registerId: string; productName: string; brandName?: string; composition?: string; packing?: string; mrp?: number; purchasePrice?: number; gstPercent?: number; hsnCode?: string; productCode?: string; manufacturer?: string; expiryDate?: string; imageFileId?: string; description?: string; customValues: Record<string, string | null>; attachments: FileInfo[] }

export default function ProductsPage() {
  const { id } = useParams()
  return id ? <ProductList registerId={id} /> : <Registers />
}

function Registers() {
  const nav = useNavigate()
  const toast = useToast()
  const regs = useAsync(() => api.get<Register[]>('/api/product-registers').then((r) => r.data), [])
  const [edit, setEdit] = useState<Partial<Register> | null>(null)
  useEffect(() => { if (regs.data?.length === 1 && !edit) nav(`/products/${regs.data[0]!.id}`, { replace: true }) }, [regs.data]) // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <>
      <PageHeader title="Product Register" subtitle="Medicines, stock, assets — with MRP, GST and expiry tracking" icon={<FiPackage />}><Button onClick={() => setEdit({ customFields: [] })}><FiPlus /> New register</Button></PageHeader>
      {regs.loading ? <Loading /> : regs.data?.length === 0 ? <Card><EmptyState icon={<FiPackage />} title="No product registers" text="Create a register like “Pharmacy stock” or “Shop inventory”." action={<Button onClick={() => setEdit({ name: 'My Products', customFields: [] })}><FiPlus /> Create register</Button>} /></Card> :
        <div className="row g-3">{regs.data?.map((r) => <div key={r.id} className="col-md-6 col-xl-4"><Card className="h-100 clickable" onClick={() => nav(`/products/${r.id}`)}><Card.Body>
          <h6 className="fw-bold">{r.name}</h6><div className="small text-muted">{r.description || '—'}</div><Badge bg="" className="badge-soft mt-2">{r.productCount} products</Badge>
        </Card.Body></Card></div>)}</div>}
      {edit && <RegisterModal value={edit} onClose={(saved) => { setEdit(null); if (saved) { void regs.reload(); toast.success('Register saved') } }} />}
    </>
  )
}

function RegisterModal({ value, onClose }: { value: Partial<Register>; onClose: (saved: boolean) => void }) {
  const toast = useToast()
  const [name, setName] = useState(value.name ?? '')
  const [description, setDescription] = useState(value.description ?? '')
  const [fields, setFields] = useState<Field[]>(value.customFields ?? [])
  const save = async () => {
    try {
      const body = { name, description, customFields: fields.filter((f) => f.name.trim()) }
      if (value.id) await api.put(`/api/product-registers/${value.id}`, body); else await api.post('/api/product-registers', body)
      onClose(true)
    } catch (x) { toast.error(x) }
  }
  return (
    <Modal show onHide={() => onClose(false)} centered>
      <Modal.Header closeButton><Modal.Title className="h5">{value.id ? 'Edit register' : 'New product register'}</Modal.Title></Modal.Header>
      <Modal.Body>
        <Form.Control className="mb-2" placeholder="Register name" value={name} onChange={(e) => setName(e.target.value)} />
        <Form.Control className="mb-3" placeholder="Description" value={description} onChange={(e) => setDescription(e.target.value)} />
        <h6 className="fw-bold small">Custom fields <span className="text-muted fw-normal">(in addition to the standard product fields)</span></h6>
        {fields.map((f, i) => <div key={i} className="d-flex gap-2 mb-2">
          <Form.Control size="sm" placeholder="Field name, e.g. Rack no." value={f.name} onChange={(e) => setFields(fields.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))} />
          <Form.Select size="sm" style={{ maxWidth: 130 }} value={f.fieldType} onChange={(e) => setFields(fields.map((x, j) => (j === i ? { ...x, fieldType: e.target.value } : x)))}>{['Text', 'Number', 'Decimal', 'Date', 'Boolean'].map((t) => <option key={t}>{t}</option>)}</Form.Select>
          <Button size="sm" variant="link" className="text-danger" onClick={() => setFields(fields.filter((_, j) => j !== i))}><FiTrash2 /></Button></div>)}
        <Button size="sm" variant="outline-primary" onClick={() => setFields([...fields, { name: '', fieldType: 'Text' }])}><FiPlus /> Add field</Button>
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button disabled={!name.trim()} onClick={() => void save()}>Save</Button></Modal.Footer>
    </Modal>
  )
}

function ProductList({ registerId }: { registerId: string }) {
  const nav = useNavigate()
  const toast = useToast()
  const confirm = useConfirm()
  const [params, setParams] = useSearchParams()
  const regs = useAsync(() => api.get<Register[]>('/api/product-registers').then((r) => r.data), [])
  const reg = regs.data?.find((r) => r.id === registerId)
  const [q, setQ] = useState('')
  const search = useDebounced(q)
  const [filter, setFilter] = useState<'all' | 'expiring' | 'expired'>('all')
  const [sort, setSort] = useState('name')
  const [page, setPage] = useState(1)
  const query = { search: search || undefined, expiringWithinDays: filter === 'expiring' ? 30 : undefined, expiredOnly: filter === 'expired' || undefined, sort }
  const list = useAsync(() => api.get(`/api/product-registers/${registerId}/products`, { params: { ...query, page, pageSize: 25 } }).then((r) => r.data as { items: Product[]; totalPages: number; totalCount: number }), [registerId, search, filter, sort, page])
  const [edit, setEdit] = useState<Partial<Product> | null>(null)
  const [regEdit, setRegEdit] = useState(false)
  const [importRes, setImportRes] = useState<{ imported: number; failed: number; errors: string[] } | null>(null)
  useEffect(() => {
    const open = params.get('open')
    if (open) { api.get<Product>(`/api/product-registers/${registerId}/products/${open}`).then((r) => setEdit(r.data)).catch(() => {}); params.delete('open'); setParams(params, { replace: true }) }
  }, [params, setParams, registerId])
  if (regs.loading) return <Loading />
  if (!reg) return <Alert variant="danger">Register not found.</Alert>
  const today = new Date().toISOString().slice(0, 10)
  const soon = new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10)
  const qs = new URLSearchParams(Object.entries(query).filter(([, v]) => v !== undefined).map(([k, v]) => [k, String(v)])).toString()
  return (
    <>
      <PageHeader title={reg.name} subtitle={reg.description} icon={<FiPackage />}>
        <Button size="sm" variant="outline-secondary" onClick={() => nav('/products')}><FiArrowLeft /> Registers</Button>
        <Button size="sm" variant="outline-secondary" onClick={() => setRegEdit(true)}><FiEdit2 /> Fields</Button>
        <label className="btn btn-sm btn-outline-secondary mb-0"><FiUpload /> Import Excel<input type="file" hidden accept=".xlsx,.csv" onChange={async (e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) try { setImportRes(await uploadFile(`/api/product-registers/${registerId}/import`, f)); void list.reload() } catch (x) { toast.error(x) } }} /></label>
        <ExportMenu url={(f) => `/api/product-registers/${registerId}/export?format=${f}&${qs}`} />
        <Button size="sm" onClick={() => setEdit({ customValues: {} })}><FiPlus /> Add product</Button>
      </PageHeader>
      <Card className="mb-3"><Card.Body className="d-flex flex-wrap gap-2 align-items-center">
        <Form.Control placeholder="Search name, brand, composition, code, HSN…" value={q} onChange={(e) => { setQ(e.target.value); setPage(1) }} style={{ maxWidth: 360 }} />
        <Nav variant="pills" activeKey={filter} onSelect={(k) => { setFilter(k as typeof filter); setPage(1) }}>
          <Nav.Item><Nav.Link eventKey="all">All</Nav.Link></Nav.Item><Nav.Item><Nav.Link eventKey="expiring">Expiring in 30 days</Nav.Link></Nav.Item><Nav.Item><Nav.Link eventKey="expired">Expired</Nav.Link></Nav.Item></Nav>
        <Form.Select size="sm" value={sort} onChange={(e) => setSort(e.target.value)} style={{ maxWidth: 170 }} className="ms-auto"><option value="name">Sort: name</option><option value="expiry">Sort: expiry</option><option value="mrp">Sort: MRP</option><option value="recent">Sort: recent</option></Form.Select>
        {regs.data && regs.data.length > 1 && <Form.Select size="sm" value={registerId} onChange={(e) => nav(`/products/${e.target.value}`)} style={{ maxWidth: 200 }}>{regs.data.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}</Form.Select>}
      </Card.Body></Card>
      <Card>
        {list.loading && !list.data ? <Loading /> : list.data?.items.length === 0 ? <EmptyState icon={<FiPackage />} title="No products" text="Add products manually or import an Excel sheet with columns like Product Name, Brand Name, MRP, GST %, Expiry Date." /> : (
          <Table responsive hover className="mb-0 align-middle small">
            <thead><tr><th>Product</th><th>Brand / Manufacturer</th><th>Packing</th><th className="text-end">MRP</th><th className="text-end">GST</th><th>HSN</th><th>Expiry</th><th /></tr></thead>
            <tbody>{list.data?.items.map((p) => (
              <tr key={p.id} className="clickable" onClick={() => setEdit(p)}>
                <td><div className="d-flex align-items-center gap-2"><Avatar name={p.productName} src={p.imageFileId ? `/api/files/${p.imageFileId}?inline=true` : null} size={34} /><div><div className="fw-medium">{p.productName}</div><div className="text-muted">{p.composition}</div></div></div></td>
                <td>{p.brandName}<div className="text-muted">{p.manufacturer}</div></td><td>{p.packing}</td><td className="text-end">{money(p.mrp)}</td><td className="text-end">{p.gstPercent != null ? `${p.gstPercent}%` : '—'}</td><td>{p.hsnCode}</td>
                <td>{p.expiryDate ? <Badge bg="" className={p.expiryDate < today ? 'bg-soft-danger' : p.expiryDate <= soon ? 'bg-soft-warning' : 'badge-soft'}>{date(p.expiryDate)}</Badge> : '—'}</td>
                <td className="text-end"><Button size="sm" variant="link" className="text-danger" onClick={async (e) => { e.stopPropagation(); if (await confirm({ title: `Delete ${p.productName}?` })) { await api.delete(`/api/product-registers/${registerId}/products/${p.id}`); void list.reload() } }}><FiTrash2 /></Button></td>
              </tr>))}</tbody></Table>)}
        <Card.Footer className="d-flex justify-content-between"><small className="text-muted">{list.data?.totalCount ?? 0} product(s)</small><Pager page={page} totalPages={list.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer>
      </Card>
      {edit && <ProductModal register={reg} product={edit} onClose={(ok) => { setEdit(null); if (ok) void list.reload() }} />}
      {regEdit && <RegisterModal value={reg} onClose={(ok) => { setRegEdit(false); if (ok) void regs.reload() }} />}
      <Modal show={!!importRes} onHide={() => setImportRes(null)} centered><Modal.Header closeButton><Modal.Title className="h5">Import result</Modal.Title></Modal.Header>
        <Modal.Body><Badge bg="success">{importRes?.imported} imported</Badge> {importRes?.failed ? <Badge bg="danger">{importRes.failed} failed</Badge> : null}{importRes?.errors.map((e) => <div key={e} className="small text-danger mt-1">{e}</div>)}</Modal.Body></Modal>
    </>
  )
}

function ProductModal({ register, product, onClose }: { register: Register; product: Partial<Product>; onClose: (ok: boolean) => void }) {
  const toast = useToast()
  const [p, setP] = useState<Partial<Product>>({ ...product, customValues: { ...(product.customValues ?? {}) } })
  const [atts, setAtts] = useState<FileInfo[]>(product.attachments ?? [])
  const [preview, setPreview] = useState<FileInfo | null>(null)
  const [busy, setBusy] = useState(false)
  const set = (k: keyof Product) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setP({ ...p, [k]: e.target.value })
  const num = (v: unknown) => (v === '' || v === undefined || v === null ? null : Number(v))
  const save = async () => {
    setBusy(true)
    try {
      const body = { ...p, mrp: num(p.mrp), purchasePrice: num(p.purchasePrice), gstPercent: num(p.gstPercent), expiryDate: p.expiryDate || null, imageFileId: p.imageFileId || null }
      if (product.id) await api.put(`/api/product-registers/${register.id}/products/${product.id}`, body); else await api.post(`/api/product-registers/${register.id}/products`, body)
      toast.success('Product saved'); onClose(true)
    } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  const field = (k: keyof Product, label: string, type = 'text', cls = 'col-md-6') => <Form.Group className={cls}><Form.Label className="small fw-semibold">{label}</Form.Label><Form.Control type={type} step="any" value={(p[k] as string | number | undefined) ?? ''} onChange={set(k)} /></Form.Group>
  return (
    <Modal show onHide={() => onClose(false)} size="lg" centered>
      <Modal.Header closeButton><Modal.Title className="h5">{product.id ? 'Edit product' : 'Add product'}</Modal.Title></Modal.Header>
      <Modal.Body className="row g-3">
        <div className="col-12 d-flex align-items-center gap-3">
          <Avatar name={p.productName} src={p.imageFileId ? `/api/files/${p.imageFileId}?inline=true` : null} size={64} />
          <label className="btn btn-sm btn-outline-primary mb-0">Upload image<input type="file" hidden accept="image/png,image/jpeg,image/webp" onChange={async (e) => { const f = e.target.files?.[0]; if (!f) return; try { const r = await uploadFile<{ id: string }>('/api/product-registers/images', f); setP({ ...p, imageFileId: r.id }) } catch (x) { toast.error(x) } }} /></label>
        </div>
        {field('productName', 'Product name *')}{field('brandName', 'Brand name')}{field('composition', 'Composition', 'text', 'col-12')}
        {field('packing', 'Packing', 'text', 'col-md-4')}{field('mrp', 'MRP (₹)', 'number', 'col-md-4')}{field('purchasePrice', 'Purchase price (₹)', 'number', 'col-md-4')}
        {field('gstPercent', 'GST %', 'number', 'col-md-4')}{field('hsnCode', 'HSN code', 'text', 'col-md-4')}{field('productCode', 'Product code', 'text', 'col-md-4')}
        {field('manufacturer', 'Manufacturer')}{field('expiryDate', 'Expiry date', 'date')}
        {register.customFields.map((f) => <Form.Group key={f.id} className="col-md-6"><Form.Label className="small fw-semibold">{f.name}</Form.Label>
          {f.fieldType === 'Boolean' ? <Form.Select value={p.customValues?.[f.id!] ?? ''} onChange={(e) => setP({ ...p, customValues: { ...p.customValues, [f.id!]: e.target.value } })}><option value="">—</option><option value="true">Yes</option><option value="false">No</option></Form.Select>
            : <Form.Control type={f.fieldType === 'Date' ? 'date' : ['Number', 'Decimal'].includes(f.fieldType) ? 'number' : 'text'} step="any" value={p.customValues?.[f.id!] ?? ''} onChange={(e) => setP({ ...p, customValues: { ...p.customValues, [f.id!]: e.target.value } })} />}</Form.Group>)}
        <Form.Group className="col-12"><Form.Label className="small fw-semibold">Description</Form.Label><Form.Control as="textarea" rows={2} value={p.description ?? ''} onChange={set('description')} /></Form.Group>
        {product.id && <div className="col-12"><div className="d-flex justify-content-between"><span className="small fw-semibold">Attachments</span>
          <label className="btn btn-sm btn-outline-primary mb-0"><FiPaperclip /> Attach<input type="file" hidden onChange={async (e) => { const f = e.target.files?.[0]; if (!f) return; try { setAtts([...atts, await uploadFile<FileInfo>(`/api/product-registers/${register.id}/products/${product.id}/attachments`, f)]) } catch (x) { toast.error(x) } }} /></label></div>
          <AttachmentList files={atts} urlFor={(f) => `/api/files/${f.id}`} onPreview={setPreview} onRemove={async (f) => { await api.delete(`/api/product-registers/${register.id}/products/${product.id}/attachments/${f.id}`); setAtts(atts.filter((a) => a.id !== f.id)) }} /></div>}
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button disabled={busy || !p.productName} onClick={() => void save()}>{busy ? <Spinner size="sm" /> : 'Save'}</Button></Modal.Footer>
      <PreviewModal url={preview ? `/api/files/${preview.id}?inline=true` : null} name={preview?.name} contentType={preview?.contentType} onHide={() => setPreview(null)} />
    </Modal>
  )
}
