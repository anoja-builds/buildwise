import { Navigate, Route, Routes } from 'react-router-dom';
import AppShell from './components/AppShell';
import RequestListPage from './pages/RequestListPage';
import RequestFormPage from './pages/RequestFormPage';
import RequestDetailPage from './pages/RequestDetailPage';
import ApprovalQueuePage from './pages/ApprovalQueuePage';
import { AuthProvider } from './context/AuthContext';

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/" element={<Navigate to="/requests" replace />} />
          <Route path="/requests" element={<RequestListPage />} />
          <Route path="/requests/new" element={<RequestFormPage mode="create" />} />
          <Route path="/requests/:id" element={<RequestDetailPage />} />
          <Route path="/requests/:id/edit" element={<RequestFormPage mode="edit" />} />
          <Route path="/approvals" element={<ApprovalQueuePage />} />
          <Route path="*" element={<Navigate to="/requests" replace />} />
        </Route>
      </Routes>
    </AuthProvider>
  );
}
