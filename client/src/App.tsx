import { Routes, Route, Navigate } from 'react-router-dom';
import { useAuthStore } from './lib/auth';
import { ToastHost } from './lib/toast';
import { ConfirmDialogHost } from './lib/confirmDialog';
import { KeyboardPalette } from './components/KeyboardPalette';
import LandingPage from './pages/LandingPage';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import DashboardPage from './pages/DashboardPage';
import ProfilePage from './pages/ProfilePage';
import ProjectLayout from './pages/ProjectLayout';
import BoardPage from './pages/BoardPage';
import EpicsPage from './pages/EpicsPage';
import EpicDetailPage from './pages/EpicDetailPage';
import SprintsPage from './pages/SprintsPage';
import SprintDetailPage from './pages/SprintDetailPage';
import LabelsPage from './pages/LabelsPage';
import IssuesPage from './pages/IssuesPage';
import ActivityPage from './pages/ActivityPage';
import MembersPage from './pages/MembersPage';
import SettingsLayout, { SettingsGeneralPage } from './pages/SettingsLayout';

function RequireAuth({ children }: { children: JSX.Element }) {
  const token = useAuthStore((s) => s.accessToken);
  return token ? children : <Navigate to="/login" replace />;
}

export default function App() {
  return (
    <>
      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route
          path="/dashboard"
          element={
            <RequireAuth>
              <DashboardPage />
            </RequireAuth>
          }
        />
        <Route
          path="/profile"
          element={
            <RequireAuth>
              <ProfilePage />
            </RequireAuth>
          }
        />
        <Route
          path="/p/:slug"
          element={
            <RequireAuth>
              <ProjectLayout />
            </RequireAuth>
          }
        >
          <Route index element={<Navigate to="board" replace />} />
          <Route path="board" element={<BoardPage />} />
          <Route path="issues" element={<IssuesPage />} />
          <Route path="epics" element={<EpicsPage />} />
          <Route path="epics/:id" element={<EpicDetailPage />} />
          <Route path="sprints" element={<SprintsPage />} />
          <Route path="sprints/:id" element={<SprintDetailPage />} />
          <Route path="activity" element={<ActivityPage />} />
          {/* Legacy direct /labels link still works — redirects into the
              new Settings shell so external bookmarks don't 404. */}
          <Route path="labels" element={<Navigate to="../settings/labels" replace />} />
          <Route path="settings" element={<SettingsLayout />}>
            <Route index element={<Navigate to="members" replace />} />
            <Route path="members" element={<MembersPage />} />
            <Route path="labels" element={<LabelsPage />} />
            <Route path="general" element={<SettingsGeneralPage />} />
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      <ToastHost />
      <ConfirmDialogHost />
      <KeyboardPalette />
    </>
  );
}
