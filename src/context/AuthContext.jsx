import { createContext, useContext, useMemo, useState } from 'react';
import { users, ROLES } from '../data/mockData';

// NOTE: Authentication/authorization is a shared, mandatory feature
// owned outside Component 1 (per the assignment brief). This context
// only stands in for "who is logged in" so Component 1's screens can
// be demoed and role-gated in isolation. Replace with the real
// shared auth context/JWT once it exists.

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [currentUserId, setCurrentUserId] = useState(users[0].id);

  const currentUser = useMemo(
    () => users.find((u) => u.id === currentUserId) || users[0],
    [currentUserId]
  );

  const value = {
    currentUser,
    users,
    setCurrentUserId,
    isProcurementManager: currentUser.role === ROLES.PROCUREMENT_MANAGER,
    isSiteEngineer: currentUser.role === ROLES.SITE_ENGINEER,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider');
  return ctx;
}
