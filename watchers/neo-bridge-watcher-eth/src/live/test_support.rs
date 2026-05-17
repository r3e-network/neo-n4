//! Test support for the live JSON-RPC adapters. Only compiled in test
//! builds — used by `eth_rpc` and `neo_rpc` tests to drive their
//! production HTTP code paths against an in-process fake server.
//!
//! The `FakeRpcServer` binds to a random loopback port, dispatches
//! each incoming HTTP POST to a caller-supplied handler closure
//! (which inspects the body and returns the JSON response), and
//! tears down on `Drop`.

use std::io::{Read, Write};
use std::net::TcpListener;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;

pub(crate) struct FakeRpcServer {
    pub(crate) url: String,
    stop: Arc<AtomicBool>,
    _handle: thread::JoinHandle<()>,
}

impl FakeRpcServer {
    pub(crate) fn spawn<F>(handler: F) -> Self
    where
        F: Fn(&str) -> String + Send + 'static,
    {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        listener.set_nonblocking(true).unwrap();
        let port = listener.local_addr().unwrap().port();
        let url = format!("http://127.0.0.1:{}/", port);
        let stop = Arc::new(AtomicBool::new(false));
        let stop_c = stop.clone();
        let handle = thread::spawn(move || {
            while !stop_c.load(Ordering::Relaxed) {
                match listener.accept() {
                    Ok((mut stream, _)) => {
                        // The accepted stream inherits non-blocking
                        // mode from the listener; flip back to blocking
                        // for a clean read+write cycle.
                        let _ = stream.set_nonblocking(false);
                        let mut buf = vec![0u8; 8192];
                        let n = stream.read(&mut buf).unwrap_or(0);
                        let req = String::from_utf8_lossy(&buf[..n]).to_string();
                        let body = req.split("\r\n\r\n").nth(1).unwrap_or("").to_string();
                        let resp = handler(&body);
                        let http = format!(
                            "HTTP/1.1 200 OK\r\n\
                             Content-Type: application/json\r\n\
                             Content-Length: {}\r\n\
                             Connection: close\r\n\
                             \r\n{}",
                            resp.len(),
                            resp
                        );
                        let _ = stream.write_all(http.as_bytes());
                    }
                    Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                        thread::sleep(std::time::Duration::from_millis(20));
                    }
                    Err(_) => break,
                }
            }
        });
        Self {
            url,
            stop,
            _handle: handle,
        }
    }
}

impl Drop for FakeRpcServer {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
    }
}
