import { useEffect } from 'react';

export default function Toast({ message, onDismiss }) {
  useEffect(() => {
    if (!message) return undefined;
    const timer = setTimeout(onDismiss, 3200);
    return () => clearTimeout(timer);
  }, [message, onDismiss]);

  if (!message) return null;
  return <div className="toast">{message}</div>;
}
