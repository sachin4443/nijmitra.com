import { lazy, Suspense, type ReactNode } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { FiActivity, FiAlertTriangle, FiDatabase, FiRadio, FiSettings, FiShield, FiUsers } from 'react-icons/fi'
import { AuthProvider, useAuth } from './contexts/AuthContext'
import { ThemeProvider } from './contexts/ThemeContext'
import { ToastProvider } from './contexts/ToastContext'
import { RealtimeProvider } from './contexts/RealtimeContext'
import { VaultProvider } from './contexts/VaultContext'
import { LanguageProvider } from './lib/i18n'
import { ConfirmProvider, Loading, ReauthProvider } from './components/ui'
import Layout from './components/Layout'
import { api } from './lib/api'
import LandingPage from './pages/public/LandingPage'
import LoginPage from './pages/auth/LoginPage'
import RegisterPage from './pages/auth/RegisterPage'
import ForgotPasswordPage from './pages/auth/ForgotPasswordPage'

const DashboardPage = lazy(() => import('./pages/dashboard/DashboardPage'))
const NotificationsPage = lazy(() => import('./pages/dashboard/NotificationsPage'))
const FinancePage = lazy(() => import('./pages/finance/FinancePage'))
const DocumentsPage = lazy(() => import('./pages/documents/DocumentsPage'))
const PasswordsPage = lazy(() => import('./pages/passwords/PasswordsPage'))
const TablesPage = lazy(() => import('./pages/tables/TablesPage'))
const ProductsPage = lazy(() => import('./pages/products/ProductsPage'))
const ContactsPage = lazy(() => import('./pages/people/ContactsPage'))
const UdharPage = lazy(() => import('./pages/people/UdharPage'))
const DiaryPage = lazy(() => import('./pages/journal/DiaryPage'))
const CalendarPage = lazy(() => import('./pages/journal/CalendarPage'))
const RemindersPage = lazy(() => import('./pages/journal/RemindersPage'))
const ChatPage = lazy(() => import('./pages/chat/ChatPage'))
const ProfilePage = lazy(() => import('./pages/profile/ProfilePage'))
const SettingsPage = lazy(() => import('./pages/settings/SettingsPages'))
const Admin = {
  Dashboard: lazy(() => import('./pages/admin/AdminPages').then((m) => ({ default: m.AdminDashboard }))),
  Users: lazy(() => import('./pages/admin/AdminPages').then((m) => ({ default: m.AdminUsers }))),
  Roles: lazy(() => import('./pages/admin/AdminPages').then((m) => ({ default: m.AdminRoles }))),
  Settings: lazy(() => import('./pages/admin/AdminPages').then((m) => ({ default: m.AdminSettings }))),
  Database: lazy(() => import('./pages/admin/AdminPages').then((m) => ({ default: m.AdminDatabase }))),
  Audit: lazy(() => import('./pages/admin/AdminPages').then((m) => ({ default: m.AdminAudit }))),
  Reports: lazy(() => import('./pages/admin/AdminPages').then((m) => ({ default: m.AdminReports }))),
  Communication: lazy(() => import('./pages/admin/CommunicationPages')),
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  const loc = useLocation()
  if (status === 'loading') return <div className="boot-splash"><img src="/branding/mark.webp" alt="NijMitra" height={90} /><div>NijMitra</div></div>
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: loc.pathname + loc.search }} />
  return <>{children}</>
}

function PublicOnly({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  if (status === 'loading') return <Loading />
  return status === 'authenticated' ? <Navigate to="/" replace /> : <>{children}</>
}

function Home() {
  const { status } = useAuth()
  if (status === 'loading') return <div className="boot-splash"><img src="/branding/mark.webp" alt="NijMitra" height={90} /><div>NijMitra</div></div>
  return status === 'authenticated' ? <Layout /> : <LandingPage />
}

function RequirePermission({ perm, children }: { perm?: string; children: ReactNode }) {
  const { can, isAdmin } = useAuth()
  if (!isAdmin || (perm && !can(perm))) return <div className="alert alert-warning">You don't have permission to view this page.</div>
  return <>{children}</>
}

const ADMIN_NAV = [
  { to: '/admin', label: 'Dashboard', icon: <FiActivity />, end: true },
  { to: '/admin/users', label: 'Users', icon: <FiUsers /> },
  { to: '/admin/roles', label: 'Roles & permissions', icon: <FiShield /> },
  { to: '/admin/communication', label: 'Communication Center', icon: <FiRadio /> },
  { to: '/admin/settings', label: 'Platform settings', icon: <FiSettings /> },
  { to: '/admin/database', label: 'Database', icon: <FiDatabase /> },
  { to: '/admin/audit', label: 'Audit logs', icon: <FiShield /> },
  { to: '/admin/reports', label: 'User reports', icon: <FiAlertTriangle /> },
]

const S = ({ children }: { children: ReactNode }) => <Suspense fallback={<Loading />}>{children}</Suspense>

export default function App() {
  return (
    <ThemeProvider onChange={(t) => { api.put('/api/me/theme', { theme: t }).catch(() => {}) }}>
      <LanguageProvider>
      <ToastProvider>
        <AuthProvider>
          <RealtimeProvider>
            <VaultProvider>
              <ConfirmProvider>
                <ReauthProvider>
                  <BrowserRouter>
                    <Routes>
                      <Route path="/login" element={<PublicOnly><LoginPage /></PublicOnly>} />
                      <Route path="/register" element={<PublicOnly><RegisterPage /></PublicOnly>} />
                      <Route path="/forgot-password" element={<PublicOnly><ForgotPasswordPage /></PublicOnly>} />
                      <Route path="/" element={<Home />}>
                        <Route index element={<S><DashboardPage /></S>} />
                      </Route>
                      <Route element={<RequireAuth><Layout /></RequireAuth>}>
                        <Route path="/notifications" element={<S><NotificationsPage /></S>} />
                        <Route path="/finance" element={<Navigate to="/finance/expenses" replace />} />
                        <Route path="/finance/:section" element={<S><FinancePage /></S>} />
                        <Route path="/documents" element={<S><DocumentsPage /></S>} />
                        <Route path="/passwords" element={<S><PasswordsPage /></S>} />
                        <Route path="/tables" element={<S><TablesPage /></S>} />
                        <Route path="/tables/:id" element={<S><TablesPage /></S>} />
                        <Route path="/products" element={<S><ProductsPage /></S>} />
                        <Route path="/products/:id" element={<S><ProductsPage /></S>} />
                        <Route path="/contacts" element={<S><ContactsPage /></S>} />
                        <Route path="/contacts/:id" element={<S><ContactsPage /></S>} />
                        <Route path="/udhar" element={<S><UdharPage /></S>} />
                        <Route path="/diary" element={<S><DiaryPage /></S>} />
                        <Route path="/diary/:id" element={<S><DiaryPage /></S>} />
                        <Route path="/calendar" element={<S><CalendarPage /></S>} />
                        <Route path="/reminders" element={<S><RemindersPage /></S>} />
                        <Route path="/chat" element={<S><ChatPage /></S>} />
                        <Route path="/chat/:conversationId" element={<S><ChatPage /></S>} />
                        <Route path="/profile" element={<S><ProfilePage /></S>} />
                        <Route path="/settings" element={<Navigate to="/settings/general" replace />} />
                        <Route path="/settings/:tab" element={<S><SettingsPage /></S>} />
                      </Route>
                      <Route path="/admin" element={<RequireAuth><RequirePermission><Layout admin nav={ADMIN_NAV} /></RequirePermission></RequireAuth>}>
                        <Route index element={<S><RequirePermission perm="admin.dashboard.view"><Admin.Dashboard /></RequirePermission></S>} />
                        <Route path="users" element={<S><RequirePermission perm="admin.users.view"><Admin.Users /></RequirePermission></S>} />
                        <Route path="users/:id" element={<S><RequirePermission perm="admin.users.view"><Admin.Users /></RequirePermission></S>} />
                        <Route path="roles" element={<S><RequirePermission perm="admin.users.view"><Admin.Roles /></RequirePermission></S>} />
                        <Route path="communication" element={<Navigate to="/admin/communication/providers" replace />} />
                        <Route path="communication/:section" element={<S><RequirePermission perm="admin.communication.logs"><Admin.Communication /></RequirePermission></S>} />
                        <Route path="settings" element={<S><RequirePermission perm="admin.settings.manage"><Admin.Settings /></RequirePermission></S>} />
                        <Route path="database" element={<S><RequirePermission perm="admin.database.manage"><Admin.Database /></RequirePermission></S>} />
                        <Route path="audit" element={<S><RequirePermission perm="admin.audit.view"><Admin.Audit /></RequirePermission></S>} />
                        <Route path="reports" element={<S><RequirePermission perm="admin.reports.manage"><Admin.Reports /></RequirePermission></S>} />
                      </Route>
                      <Route path="*" element={<Navigate to="/" replace />} />
                    </Routes>
                  </BrowserRouter>
                </ReauthProvider>
              </ConfirmProvider>
            </VaultProvider>
          </RealtimeProvider>
        </AuthProvider>
      </ToastProvider>
      </LanguageProvider>
    </ThemeProvider>
  )
}
