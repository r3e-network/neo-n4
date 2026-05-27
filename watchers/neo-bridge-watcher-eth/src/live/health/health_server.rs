use std::net::TcpListener;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;

use super::{handle_request, HealthState};

/// HTTP server exposing the health endpoints. Holds the listener +
/// background thread; teardown via `Drop` (sets a stop flag and waits
/// for the next accept-loop iteration to exit).
///
/// **Security note**: Endpoints are unauthenticated. In production, bind
/// to a private address (127.0.0.1 or ClusterIP) and front with a reverse
/// proxy or k8s network policy. The `/metrics` and `/info` endpoints expose
/// internal daemon state (cursor, submission count, last error).
pub struct HealthServer {
    /// Resolved bind address — useful for tests that bind to
    /// `127.0.0.1:0` and want to know which random port the OS assigned.
    pub bound_addr: std::net::SocketAddr,
    stop: Arc<AtomicBool>,
    _handle: thread::JoinHandle<()>,
}

impl HealthServer {
    /// Spawn the health server. The listener binds immediately
    /// (returns an io::Error on bind failure); the handler thread
    /// starts in the background.
    pub fn spawn(
        bind: &str,
        state: HealthState,
        healthy_threshold_secs: u64,
    ) -> std::io::Result<Self> {
        let listener = TcpListener::bind(bind)?;
        let bound_addr = listener.local_addr()?;
        listener.set_nonblocking(true)?;
        let stop = Arc::new(AtomicBool::new(false));
        let stop_c = stop.clone();
        let handle = thread::spawn(move || {
            while !stop_c.load(Ordering::Relaxed) {
                match listener.accept() {
                    Ok((mut stream, _)) => {
                        let _ = stream.set_nonblocking(false);
                        handle_request(&mut stream, &state, healthy_threshold_secs);
                    }
                    Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                        thread::sleep(std::time::Duration::from_millis(50));
                    }
                    Err(_) => break,
                }
            }
        });
        Ok(Self {
            bound_addr,
            stop,
            _handle: handle,
        })
    }
}

impl Drop for HealthServer {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
        // Join the background thread to ensure it exits before the server is
        // dropped. The thread checks the stop flag every ~50ms, so the join
        // should complete quickly. This prevents the thread from outliving the
        // HealthServer struct and potentially competing on the same port.
        let _ = self._handle.join();
    }
}
