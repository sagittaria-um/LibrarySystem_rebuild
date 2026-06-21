import {
  AlertTriangle,
  BarChart3,
  BookOpen,
  CircleDollarSign,
  ClipboardList,
  Library,
  LogOut,
  Plus,
  RefreshCw,
  Save,
  Search,
  Trash2,
  Undo2,
  UserCog,
  Users,
} from 'lucide-react';
import type * as React from 'react';
import { Component, lazy, Suspense, useEffect, useState } from 'react';
import type { LucideIcon } from 'lucide-react';
import type { ChartPoint } from './BorrowChart';

const BorrowChart = lazy(() => import('./BorrowChart'));

const API = '/api';
let noticeSink: ((notice: { message: string; type: 'success' | 'error' }) => void) | null = null;

type Session = {
  token: string;
  username: string;
  role: 'Admin' | 'Reader';
  readerCardNo?: string;
};

type Book = {
  isbn: string;
  title: string;
  publisher: string;
  author: string;
  totalCopies: number;
  availableCopies: number;
  isBorrowable: boolean;
};

type Reader = {
  readerCardNo: string;
  name: string;
  gender: string;
  title: string;
  maxBorrowCount: number;
  borrowedCount: number;
  department: string;
  phone?: string;
  unpaidFine: number;
};

type Loan = {
  loanId: number;
  readerCardNo: string;
  readerName: string;
  isbn: string;
  bookTitle: string;
  borrowDate: string;
  loanDays: number;
  dueDate: string;
  returnDate?: string | null;
  fine: number;
  finePaid: boolean;
  remark?: string;
  status: string;
};

type Account = {
  accountId: number;
  username: string;
  role: 'Admin' | 'Reader';
  readerCardNo?: string;
  isEnabled: boolean;
};

type AccountForm = {
  username: string;
  password: string;
  isEnabled: boolean;
};

type Overdue = {
  loanId: number;
  isbn: string;
  title: string;
  readerName: string;
  readerCardNo: string;
  borrowDate: string;
  dueDate: string;
  overdueDays: number;
  estimatedFine: number;
};

type Dashboard = {
  stats: Record<string, number>;
  monthly: ChartPoint[];
  popular: { title: string; count: number }[];
};

type NavItem = {
  key: string;
  label: string;
  icon: LucideIcon;
};

const emptyBook: Book = {
  isbn: '',
  title: '',
  publisher: '',
  author: '',
  totalCopies: 1,
  availableCopies: 1,
  isBorrowable: true,
};

const emptyReader: Reader = {
  readerCardNo: '',
  name: '',
  gender: '男',
  title: '本科生',
  maxBorrowCount: 5,
  borrowedCount: 0,
  department: '',
  phone: '',
  unpaidFine: 0,
};

const emptyLoan = {
  readerCardNo: '',
  isbn: '',
  borrowDate: today(),
  loanDays: 30,
  returnDate: '',
  fine: 0,
  finePaid: true,
  remark: '',
};

const emptyAccountForm: AccountForm = {
  username: '',
  password: '',
  isEnabled: true,
};

function today() {
  return new Date().toISOString().slice(0, 10);
}

function formatDate(value?: string | null) {
  return value ? value.slice(0, 10) : '-';
}

async function request<T>(path: string, session: Session | null, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers);
  if (!(options.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json');
  }
  if (session?.token) {
    headers.set('Authorization', `Bearer ${session.token}`);
  }

  let response: Response;
  try {
    response = await fetch(`${API}${path}`, { ...options, headers });
  } catch {
    notify('网络连接失败，请确认后端服务已启动。', 'error');
    throw new Error('网络连接失败，请确认后端服务已启动。');
  }

  const text = await response.text();
  let data: unknown = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = { message: normalizeErrorMessage(text.slice(0, 180)) };
  }

  if (!response.ok) {
    const message = normalizeErrorMessage(getMessage(data) ?? response.statusText ?? '请求失败');
    notify(message, 'error');
    throw new Error(message);
  }

  return data as T;
}

function getMessage(data: unknown) {
  if (data && typeof data === 'object' && 'message' in data) {
    return String((data as { message?: unknown }).message ?? '');
  }

  return '';
}

function normalizeErrorMessage(value: string) {
  const text = value.trim();
  if (!text) return text;

  const stackMarker = ' at Microsoft.';
  const stackIndex = text.indexOf(stackMarker);
  const withoutStack = stackIndex >= 0 ? text.slice(0, stackIndex).trim() : text;

  const triggerMarker = 'The transaction ended in the trigger.';
  const triggerIndex = withoutStack.indexOf(triggerMarker);
  const withoutTrigger = triggerIndex >= 0 ? withoutStack.slice(0, triggerIndex).trim() : withoutStack;

  return withoutTrigger
    .replace(/^Microsoft\.[^:]+:\s*/i, '')
    .replace(/\s*\(0x[0-9A-F]+\):\s*/i, '')
    .replace(/\s+/g, ' ')
    .trim();
}

function notify(message: string, type: 'success' | 'error' = 'success') {
  const notice = { message, type };
  noticeSink?.(notice);
  try {
    window.dispatchEvent(new CustomEvent('library-notice', { detail: notice }));
  } catch {
    // The direct sink above is the primary path.
  }
}

export default function App() {
  const [session, setSession] = useState<Session | null>(() => readStoredSession());

  const saveSession = (next: Session | null) => {
    setSession(next);
    if (next) {
      localStorage.setItem('library-session', JSON.stringify(next));
    } else {
      localStorage.removeItem('library-session');
    }
  };

  if (!session) {
    return <LoginPage onLogin={saveSession} />;
  }

  return (
    <ErrorBoundary onReset={() => saveSession(null)}>
      <Shell session={session} onLogout={() => saveSession(null)} />
    </ErrorBoundary>
  );
}

function readStoredSession() {
  try {
    const raw = window.localStorage.getItem('library-session');
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<Session>;
    if (!value.token || !value.username || (value.role !== 'Admin' && value.role !== 'Reader')) {
      window.localStorage.removeItem('library-session');
      return null;
    }
    return value as Session;
  } catch {
    return null;
  }
}

class ErrorBoundary extends Component<{ children: React.ReactNode; onReset: () => void }, { error: string | null }> {
  state = { error: null };

  static getDerivedStateFromError(error: unknown) {
    return { error: error instanceof Error ? error.message : '页面渲染失败。' };
  }

  render() {
    if (this.state.error) {
      return (
        <main className="login-page">
          <section className="login-panel">
            <div className="brand-mark">
              <AlertTriangle size={30} />
            </div>
            <h1>页面加载失败</h1>
            <p>{this.state.error}</p>
            <button className="primary-button" onClick={this.props.onReset}>返回登录</button>
          </section>
        </main>
      );
    }

    return this.props.children;
  }
}

function LoginPage({ onLogin }: { onLogin: (session: Session) => void }) {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('admin123');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError('');
    try {
      const data = await request<Session>('/auth/login', null, {
        method: 'POST',
        body: JSON.stringify({ username, password }),
      });
      onLogin(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : '登录失败');
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="login-page">
      <section className="login-panel">
        <div className="brand-mark">
          <Library size={30} />
        </div>
        <h1>中山大学深圳校区图书管理系统</h1>
        <p>管理员：admin / admin123；读者：2024001 / reader123</p>
        <form onSubmit={submit} className="login-form">
          <label>
            账号
            <input value={username} onChange={(event) => setUsername(event.target.value)} />
          </label>
          <label>
            密码
            <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} />
          </label>
          {error && <div className="error-line">{error}</div>}
          <button className="primary-button" disabled={loading}>
            {loading ? '登录中' : '登录'}
          </button>
        </form>
      </section>
    </main>
  );
}

function Shell({ session, onLogout }: { session: Session; onLogout: () => void }) {
  const [active, setActive] = useState('dashboard');
  const [notice, setNotice] = useState<{ message: string; type: 'success' | 'error' } | null>(null);
  const admin = session.role === 'Admin';
  const nav: NavItem[] = [
    { key: 'dashboard', label: '仪表盘', icon: BarChart3 },
    { key: 'books', label: '图书管理', icon: BookOpen },
    admin ? { key: 'readers', label: '读者管理', icon: Users } : { key: 'profile', label: '我的信息', icon: Users },
    { key: 'loans', label: admin ? '借阅管理' : '我的借阅', icon: ClipboardList },
    { key: 'overdue', label: '逾期查询', icon: AlertTriangle },
    ...(admin ? [{ key: 'accounts', label: '账号管理', icon: UserCog }] : []),
  ];

  useEffect(() => {
    noticeSink = (next) => {
      setNotice(next);
      window.setTimeout(() => setNotice(null), 3200);
    };
    const listener = (event: Event) => {
      const detail = (event as CustomEvent<{ message: string; type: 'success' | 'error' }>).detail;
      setNotice(detail);
      window.setTimeout(() => setNotice(null), 3200);
    };
    window.addEventListener('library-notice', listener);
    return () => {
      noticeSink = null;
      window.removeEventListener('library-notice', listener);
    };
  }, []);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <Library size={24} />
          <div>
            <strong>图书管理</strong>
            <span>SYSU Shenzhen</span>
          </div>
        </div>
        <nav>
          {nav.map(({ key, label, icon: Icon }) => (
            <button key={key} className={active === key ? 'active' : ''} onClick={() => setActive(key)}>
              <Icon size={18} />
              <span>{label}</span>
            </button>
          ))}
        </nav>
      </aside>
      <section className="workspace">
        <header className="topbar">
          <div>
            <h2>{nav.find((item) => item.key === active)?.label}</h2>
            <span>{session.role === 'Admin' ? '管理员工作台' : '读者自助查询'}</span>
          </div>
          <div className="topbar-actions">
            <span className="role-badge">{session.role === 'Admin' ? '管理员' : '读者'}</span>
            <span className="username">{session.username}</span>
            <button className="icon-button" onClick={onLogout} title="退出登录">
              <LogOut size={18} />
            </button>
          </div>
        </header>
        <main className="content">
          {notice && <div className={`notice ${notice.type}`}>{notice.message}</div>}
          {active === 'dashboard' && <DashboardPage session={session} />}
          {active === 'books' && <BooksPage session={session} />}
          {active === 'readers' && <ReadersPage session={session} />}
          {active === 'profile' && <ProfilePage session={session} />}
          {active === 'loans' && <LoansPage session={session} />}
          {active === 'overdue' && <OverduePage session={session} />}
          {active === 'accounts' && <AccountsPage session={session} />}
        </main>
      </section>
    </div>
  );
}

function DashboardPage({ session }: { session: Session }) {
  const [data, setData] = useState<Dashboard | null>(null);
  const [overdue, setOverdue] = useState<Overdue[]>([]);

  useEffect(() => {
    void request<Dashboard>('/reports/dashboard', session).then(setData);
    void request<Overdue[]>('/reports/overdue', session).then(setOverdue);
  }, [session]);

  const stats = data?.stats ?? {};
  const cards = session.role === 'Admin'
    ? [
        ['馆藏种类', stats.bookKinds ?? 0],
        ['馆藏册数', stats.totalCopies ?? 0],
        ['可借册数', stats.availableCopies ?? 0],
        ['借出中', stats.currentLoans ?? 0],
        ['逾期未还', stats.overdueLoans ?? 0],
        ['未缴罚款', `¥${Number(stats.unpaidFine ?? 0).toFixed(2)}`],
      ]
    : [
        ['当前借阅', stats.currentLoans ?? 0],
        ['逾期未还', stats.overdueLoans ?? 0],
        ['未缴罚款', `¥${Number(stats.unpaidFine ?? 0).toFixed(2)}`],
        ['历史借阅', stats.totalLoans ?? 0],
      ];

  return (
    <section className="page-stack">
      <div className="metric-grid">
        {cards.map(([label, value]) => (
          <article className="metric-card" key={label}>
            <span>{label}</span>
            <strong>{value}</strong>
          </article>
        ))}
      </div>
      <div className="dashboard-grid">
        <section className="panel">
          <PanelTitle title="近六月借阅趋势" />
          <div className="chart-box">
            {data && data.monthly.length > 0 ? (
              <Suspense fallback={<EmptyState text="图表加载中" />}>
                <BorrowChart data={data.monthly} />
              </Suspense>
            ) : (
              <EmptyState text={data ? '暂无借阅趋势' : '图表加载中'} />
            )}
          </div>
        </section>
        <section className="panel">
          <PanelTitle title="逾期未还" />
          <DataTable
            columns={['ISBN', '书名', '读者', '应还日期', '罚款']}
            rows={overdue.slice(0, 6).map((item) => [
              item.isbn,
              item.title,
              item.readerName,
              formatDate(item.dueDate),
              `¥${item.estimatedFine.toFixed(2)}`,
            ])}
          />
        </section>
      </div>
      {session.role === 'Admin' && (
        <section className="panel">
          <PanelTitle title="热门借阅图书" />
          <DataTable
            columns={['书名', '借阅次数']}
            rows={(data?.popular ?? []).map((item) => [item.title, item.count])}
          />
        </section>
      )}
    </section>
  );
}

function BooksPage({ session }: { session: Session }) {
  const [items, setItems] = useState<Book[]>([]);
  const [q, setQ] = useState('');
  const [form, setForm] = useState<Book>(emptyBook);
  const [editing, setEditing] = useState(false);
  const admin = session.role === 'Admin';
  const canSelfBorrow = session.role === 'Reader';

  async function load() {
    setItems(await request<Book[]>(`/books?q=${encodeURIComponent(q)}`, session));
  }

  useEffect(() => {
    void load();
  }, []);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    try {
      await request(editing ? `/books/${form.isbn}` : '/books', session, {
        method: editing ? 'PUT' : 'POST',
        body: JSON.stringify(form),
      });
      setForm(emptyBook);
      setEditing(false);
      await load();
      notify(editing ? '图书信息已更新。' : '图书已新增。');
    } catch {
      // Error is shown by request().
    }
  }

  async function remove(isbn: string) {
    if (!confirm('确认删除该图书？')) return;
    try {
      await request(`/books/${isbn}`, session, { method: 'DELETE' });
      await load();
      notify('图书已删除。');
    } catch {
      // Error is shown by request().
    }
  }

  async function borrowBook(item: Book) {
    if (!item.isBorrowable || item.availableCopies <= 0) {
      notify('该图书当前不可借。', 'error');
      return;
    }

    try {
      await request('/borrow-records/borrow', session, {
        method: 'POST',
        body: JSON.stringify({
          readerCardNo: session.readerCardNo,
          isbn: item.isbn,
          borrowDate: today(),
          loanDays: 30,
        }),
      });
      await load();
      notify(`已借阅《${item.title}》。`);
    } catch {
      // Error is shown by request().
    }
  }

  return (
    <CrudLayout
      title="图书检索"
      q={q}
      setQ={setQ}
      onSearch={load}
      action={admin ? <button className="secondary-button" onClick={() => { setForm(emptyBook); setEditing(false); notify('已切换到新增图书表单。'); }}><Plus size={16} />新增</button> : null}
    >
      {admin && (
        <form className="edit-grid" onSubmit={save}>
          <Field label="ISBN"><input required disabled={editing} value={form.isbn} onChange={(event) => setForm({ ...form, isbn: event.target.value })} /></Field>
          <Field label="书名"><input required value={form.title} onChange={(event) => setForm({ ...form, title: event.target.value })} /></Field>
          <Field label="出版社"><input required value={form.publisher} onChange={(event) => setForm({ ...form, publisher: event.target.value })} /></Field>
          <Field label="作者"><input required value={form.author} onChange={(event) => setForm({ ...form, author: event.target.value })} /></Field>
          <Field label="馆藏数量"><input type="number" min="0" value={form.totalCopies} onChange={(event) => setForm({ ...form, totalCopies: Number(event.target.value) })} /></Field>
          <Field label="可借数量"><input type="number" min="0" value={form.availableCopies} onChange={(event) => setForm({ ...form, availableCopies: Number(event.target.value) })} /></Field>
          <label className="check-field"><input type="checkbox" checked={form.isBorrowable} onChange={(event) => setForm({ ...form, isBorrowable: event.target.checked })} /> 可借</label>
          <button className="primary-button"><Save size={16} />保存图书</button>
        </form>
      )}
      <table className="data-table">
        <thead><tr><th>ISBN</th><th>书名</th><th>作者</th><th>出版社</th><th>馆藏</th><th>可借</th><th>状态</th>{(admin || canSelfBorrow) && <th>操作</th>}</tr></thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.isbn}>
              <td>{item.isbn}</td><td>{item.title}</td><td>{item.author}</td><td>{item.publisher}</td><td>{item.totalCopies}</td><td>{item.availableCopies}</td>
              <td><span className={item.isBorrowable ? 'tag ok' : 'tag warn'}>{item.isBorrowable ? '可借' : '不可借'}</span></td>
              {admin && <td className="row-actions"><button onClick={() => { setForm(item); setEditing(true); }}>编辑</button><button onClick={() => void remove(item.isbn)}><Trash2 size={15} /></button></td>}
              {canSelfBorrow && <td className="row-actions"><button disabled={!item.isBorrowable || item.availableCopies <= 0} onClick={() => void borrowBook(item)}>借书</button></td>}
            </tr>
          ))}
        </tbody>
      </table>
    </CrudLayout>
  );
}

function ReadersPage({ session }: { session: Session }) {
  const [items, setItems] = useState<Reader[]>([]);
  const [q, setQ] = useState('');
  const [form, setForm] = useState<Reader>(emptyReader);
  const [editing, setEditing] = useState(false);
  const [password, setPassword] = useState('');

  async function load() {
    setItems(await request<Reader[]>(`/readers?q=${encodeURIComponent(q)}`, session));
  }

  useEffect(() => {
    void load();
  }, []);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    try {
      await request(editing ? `/readers/${form.readerCardNo}` : '/readers', session, {
        method: editing ? 'PUT' : 'POST',
        body: JSON.stringify({ ...form, password: password }),
      });
      setPassword('');
      setForm(emptyReader);
      setEditing(false);
      setPassword('');
      await load();
      notify(editing ? '读者信息已更新。' : '读者已新增。');
    } catch {
      // Error is shown by request().
    }
  }

  async function remove(cardNo: string) {
    if (!confirm('确认删除该读者？')) return;
    try {
      await request(`/readers/${cardNo}`, session, { method: 'DELETE' });
      await load();
      notify('读者已删除。');
    } catch {
      // Error is shown by request().
    }
  }

  async function pay(cardNo: string) {
    try {
      await request(`/readers/${cardNo}/pay-fine`, session, { method: 'POST', body: '{}' });
      await load();
      notify('罚款状态已更新。');
    } catch {
      // Error is shown by request().
    }
  }

  return (
    <CrudLayout title="读者检索" q={q} setQ={setQ} onSearch={load}>
      <form className="edit-grid" onSubmit={save}>
        <Field label="借书证号"><input required disabled={editing} value={form.readerCardNo} onChange={(event) => setForm({ ...form, readerCardNo: event.target.value })} /></Field>
        <Field label="姓名"><input required value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></Field>
        <Field label="性别"><select value={form.gender} onChange={(event) => setForm({ ...form, gender: event.target.value })}><option>男</option><option>女</option><option>其他</option></select></Field>
        <Field label="职称"><input required value={form.title} onChange={(event) => setForm({ ...form, title: event.target.value })} /></Field>
        <Field label="可借数量"><input type="number" min="0" value={form.maxBorrowCount} onChange={(event) => setForm({ ...form, maxBorrowCount: Number(event.target.value) })} /></Field>
        <Field label="已借数量"><input type="number" min="0" value={form.borrowedCount} onChange={(event) => setForm({ ...form, borrowedCount: Number(event.target.value) })} /></Field>
        <Field label="工作部门"><input required value={form.department} onChange={(event) => setForm({ ...form, department: event.target.value })} /></Field>
        <Field label="联系电话"><input value={form.phone ?? ''} onChange={(event) => setForm({ ...form, phone: event.target.value })} /></Field>
          {!editing && (
          <Field label="初始密码">
            <input 
              type="password" 
              placeholder="留空则自动顺延短账号" 
              value={password} 
              onChange={(event) => setPassword(event.target.value)} 
            />
          </Field>
        )}
        <button className="primary-button"><Save size={16} />保存读者</button>
      </form>
      <table className="data-table">
        <thead><tr><th>借书证号</th><th>姓名</th><th>性别</th><th>职称</th><th>部门</th><th>已借/可借</th><th>未缴罚款</th><th>操作</th></tr></thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.readerCardNo}>
              <td>{item.readerCardNo}</td><td>{item.name}</td><td>{item.gender}</td><td>{item.title}</td><td>{item.department}</td><td>{item.borrowedCount}/{item.maxBorrowCount}</td><td>¥{item.unpaidFine.toFixed(2)}</td>
              <td className="row-actions"><button onClick={() => { setForm(item); setEditing(true); setPassword(''); }}>编辑</button><button onClick={() => void pay(item.readerCardNo)}><CircleDollarSign size={15} /></button><button onClick={() => void remove(item.readerCardNo)}><Trash2 size={15} /></button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </CrudLayout>
  );
}

function ProfilePage({ session }: { session: Session }) {
  const [profile, setProfile] = useState<{ reader: Reader; openLoans: Loan[] } | null>(null);

  useEffect(() => {
    if (session.readerCardNo) {
      void request<{ reader: Reader; openLoans: Loan[] }>(`/readers/${session.readerCardNo}`, session).then(setProfile);
    }
  }, [session]);

  if (!profile) return <EmptyState text="正在读取个人信息" />;

  return (
    <section className="page-stack">
      <div className="metric-grid">
        <article className="metric-card"><span>姓名</span><strong>{profile.reader.name}</strong></article>
        <article className="metric-card"><span>已借/可借</span><strong>{profile.reader.borrowedCount}/{profile.reader.maxBorrowCount}</strong></article>
        <article className="metric-card"><span>未缴罚款</span><strong>¥{profile.reader.unpaidFine.toFixed(2)}</strong></article>
      </div>
      <section className="panel">
        <PanelTitle title="未归还图书" />
        <DataTable columns={['ISBN', '书名', '借出日期', '应还日期', '状态']} rows={profile.openLoans.map((item) => [item.isbn, item.bookTitle, formatDate(item.borrowDate), formatDate(item.dueDate), item.status])} />
      </section>
    </section>
  );
}

function LoansPage({ session }: { session: Session }) {
  const [items, setItems] = useState<Loan[]>([]);
  const [books, setBooks] = useState<Book[]>([]);
  const [readers, setReaders] = useState<Reader[]>([]);
  const [q, setQ] = useState('');
  const [status, setStatus] = useState('');
  const [form, setForm] = useState(emptyLoan);
  const [editingId, setEditingId] = useState<number | null>(null);
  const admin = session.role === 'Admin';

  async function load() {
    setItems(await request<Loan[]>(`/borrow-records?q=${encodeURIComponent(q)}&status=${status}`, session));
  }

  useEffect(() => {
    void load();
    if (admin) {
      void request<Book[]>('/books', session).then(setBooks);
      void request<Reader[]>('/readers', session).then(setReaders);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [status]);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    const body = {
      ...form,
      returnDate: form.returnDate || null,
    };
    try {
      await request(editingId ? `/borrow-records/${editingId}` : '/borrow-records', session, {
        method: editingId ? 'PUT' : 'POST',
        body: JSON.stringify(body),
      });
      setForm(emptyLoan);
      setEditingId(null);
      await load();
      notify(editingId ? '借阅记录已更新。' : '借阅记录已新增。');
    } catch {
      // Error is shown by request().
    }
  }

  async function borrow() {
    const reader = readers.find((item) => item.readerCardNo === form.readerCardNo);
    if (reader && reader.unpaidFine > 0) {
      notify('该账户存在未缴欠款，不能借书。', 'error');
      return;
    }

    try {
      await request('/borrow-records/borrow', session, {
        method: 'POST',
        body: JSON.stringify({
          readerCardNo: form.readerCardNo,
          isbn: form.isbn,
          borrowDate: form.borrowDate,
          loanDays: form.loanDays,
        }),
      });
      setForm(emptyLoan);
      await load();
      notify('借书办理成功。');
    } catch {
      // Error is shown by request().
    }
  }

  async function returnBook(loanId: number) {
    try {
      await request(`/borrow-records/${loanId}/return`, session, {
        method: 'POST',
        body: JSON.stringify({ returnDate: today() }),
      });
      await load();
      notify('还书办理成功。');
    } catch {
      // Error is shown by request().
    }
  }

  async function remove(loanId: number) {
    if (!confirm('确认删除该借阅记录？')) return;
    try {
      await request(`/borrow-records/${loanId}`, session, { method: 'DELETE' });
      await load();
      notify('借阅记录已删除。');
    } catch {
      // Error is shown by request().
    }
  }

  return (
    <CrudLayout
      title="借阅检索"
      q={q}
      setQ={setQ}
      onSearch={load}
      action={<select className="compact-select" value={status} onChange={(event) => setStatus(event.target.value)}><option value="">全部</option><option value="open">借出中</option><option value="overdue">逾期</option><option value="returned">已归还</option></select>}
    >
      {admin && (
        <form className="edit-grid" onSubmit={save}>
          <Field label="读者"><select required value={form.readerCardNo} onChange={(event) => setForm({ ...form, readerCardNo: event.target.value })}><option value="">选择读者</option>{readers.map((reader) => <option key={reader.readerCardNo} value={reader.readerCardNo}>{reader.readerCardNo} {reader.name}</option>)}</select></Field>
          <Field label="图书"><select required value={form.isbn} onChange={(event) => setForm({ ...form, isbn: event.target.value })}><option value="">选择图书</option>{books.map((book) => <option key={book.isbn} value={book.isbn}>{book.title}</option>)}</select></Field>
          <Field label="借出日期"><input type="date" value={form.borrowDate} onChange={(event) => setForm({ ...form, borrowDate: event.target.value })} /></Field>
          <Field label="借阅期限"><input type="number" min="1" value={form.loanDays} onChange={(event) => setForm({ ...form, loanDays: Number(event.target.value) })} /></Field>
          <Field label="归还日期"><input type="date" value={form.returnDate} onChange={(event) => setForm({ ...form, returnDate: event.target.value })} /></Field>
          <Field label="罚款"><input type="number" min="0" step="0.5" value={form.fine} onChange={(event) => setForm({ ...form, fine: Number(event.target.value) })} /></Field>
          <label className="check-field"><input type="checkbox" checked={form.finePaid} onChange={(event) => setForm({ ...form, finePaid: event.target.checked })} /> 已缴罚款</label>
          <div className="button-row"><button className="primary-button"><Save size={16} />保存记录</button><button type="button" className="secondary-button" onClick={() => void borrow()}><Plus size={16} />办理借书</button></div>
        </form>
      )}
      <table className="data-table">
        <thead><tr><th>编号</th><th>书名</th><th>读者</th><th>借出</th><th>应还</th><th>归还</th><th>罚款</th><th>状态</th>{admin && <th>操作</th>}</tr></thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.loanId}>
              <td>{item.loanId}</td><td>{item.bookTitle}</td><td>{item.readerName}</td><td>{formatDate(item.borrowDate)}</td><td>{formatDate(item.dueDate)}</td><td>{formatDate(item.returnDate)}</td><td>¥{item.fine.toFixed(2)}</td>
              <td><span className={item.status === '逾期' ? 'tag danger' : item.status === '已归还' ? 'tag ok' : 'tag'}>{item.status}</span></td>
              {admin && <td className="row-actions"><button onClick={() => { setEditingId(item.loanId); setForm({ readerCardNo: item.readerCardNo, isbn: item.isbn, borrowDate: formatDate(item.borrowDate), loanDays: item.loanDays, returnDate: item.returnDate ? formatDate(item.returnDate) : '', fine: item.fine, finePaid: item.finePaid, remark: item.remark ?? '' }); }}>编辑</button>{!item.returnDate && <button onClick={() => void returnBook(item.loanId)}><Undo2 size={15} /></button>}<button onClick={() => void remove(item.loanId)}><Trash2 size={15} /></button></td>}
            </tr>
          ))}
        </tbody>
      </table>
    </CrudLayout>
  );
}

function AccountsPage({ session }: { session: Session }) {
  const [items, setItems] = useState<Account[]>([]);
  const [form, setForm] = useState<AccountForm>(emptyAccountForm);

  async function load() {
    setItems(await request<Account[]>('/accounts', session));
  }

  useEffect(() => {
    void load();
  }, []);

  async function createAdmin(event: React.FormEvent) {
    event.preventDefault();
    try {
      await request('/accounts', session, {
        method: 'POST',
        body: JSON.stringify({
          username: form.username,
          password: form.password,
          role: 'Admin',
          readerCardNo: null,
          isEnabled: form.isEnabled,
        }),
      });
      setForm(emptyAccountForm);
      await load();
      notify('管理员账号已新增。');
    } catch {
      // Error is shown by request().
    }
  }

  async function remove(accountId: number) {
    if (!confirm('确认删除该账号？')) return;
    try {
      await request(`/accounts/${accountId}`, session, { method: 'DELETE' });
      await load();
      notify('账号已删除。');
    } catch {
      // Error is shown by request().
    }
  }

  return (
    <section className="page-stack">
      <section className="panel">
        <PanelTitle title="新增管理员账号" />
        <form className="edit-grid" onSubmit={createAdmin}>
          <Field label="账号"><input required value={form.username} onChange={(event) => setForm({ ...form, username: event.target.value })} /></Field>
          <Field label="初始密码"><input required type="password" value={form.password} onChange={(event) => setForm({ ...form, password: event.target.value })} /></Field>
          <label className="check-field"><input type="checkbox" checked={form.isEnabled} onChange={(event) => setForm({ ...form, isEnabled: event.target.checked })} /> 启用账号</label>
          <div className="button-row"><button className="primary-button"><Plus size={16} />新增管理员</button></div>
        </form>
      </section>
      <table className="data-table">
        <thead>
          <tr><th>账号</th><th>角色</th><th>关联读者证号</th><th>状态</th><th>操作</th></tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.accountId}>
              <td>{item.username}</td>
              <td>{item.role}</td>
              <td>{item.readerCardNo ?? '-'}</td>
              <td><span className={item.isEnabled ? 'tag ok' : 'tag warn'}>{item.isEnabled ? '启用' : '禁用'}</span></td>
              <td className="row-actions">
                {/* 🟢 只保留删除按钮。因为开户和信息修改已经全部移到了“读者管理”页统一维护 */}
                <button onClick={() => void remove(item.accountId)}><Trash2 size={15} /></button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

function OverduePage({ session }: { session: Session }) {
  const [items, setItems] = useState<Overdue[]>([]);

  useEffect(() => {
    void request<Overdue[]>('/reports/overdue', session).then(setItems);
  }, [session]);

  return (
    <section className="panel">
      <PanelTitle title="到期未还图书" />
      <DataTable columns={['记录号', 'ISBN', '书名', '读者', '借出日期', '应还日期', '逾期天数', '预计罚款']} rows={items.map((item) => [item.loanId, item.isbn, item.title, `${item.readerName} / ${item.readerCardNo}`, formatDate(item.borrowDate), formatDate(item.dueDate), item.overdueDays, `¥${item.estimatedFine.toFixed(2)}`])} />
    </section>
  );
}

function CrudLayout({
  title,
  q,
  setQ,
  onSearch,
  action,
  children,
}: {
  title: string;
  q: string;
  setQ: (value: string) => void;
  onSearch: () => void;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section className="page-stack">
      <div className="toolbar panel">
        <div className="search-box">
          <Search size={17} />
          <input placeholder={title} value={q} onChange={(event) => setQ(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter') onSearch(); }} />
        </div>
        <button className="secondary-button" onClick={onSearch}><RefreshCw size={16} />刷新</button>
        {action}
      </div>
      <section className="panel table-panel">{children}</section>
    </section>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="field"><span>{label}</span>{children}</label>;
}

function PanelTitle({ title }: { title: string }) {
  return <div className="panel-title"><h3>{title}</h3></div>;
}

function DataTable({ columns, rows }: { columns: string[]; rows: React.ReactNode[][] }) {
  if (rows.length === 0) {
    return <EmptyState text="暂无数据" />;
  }

  return (
    <table className="data-table">
      <thead><tr>{columns.map((column) => <th key={column}>{column}</th>)}</tr></thead>
      <tbody>
        {rows.map((row, index) => (
          <tr key={index}>{row.map((cell, cellIndex) => <td key={cellIndex}>{cell}</td>)}</tr>
        ))}
      </tbody>
    </table>
  );
}

function EmptyState({ text }: { text: string }) {
  return <div className="empty-state">{text}</div>;
}
