use std::io::{Read, Write};
use std::net::TcpListener;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread;
use std::time::Duration;

pub struct FakeRpcServer {
    pub url: String,
    stop: Arc<AtomicBool>,
    _handle: thread::JoinHandle<()>,
}

impl FakeRpcServer {
    pub fn spawn<F>(handler: F) -> Self
    where
        F: Fn(&str) -> String + Send + 'static,
    {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        listener.set_nonblocking(true).unwrap();
        let port = listener.local_addr().unwrap().port();
        let url = format!("http://127.0.0.1:{port}/");
        let stop = Arc::new(AtomicBool::new(false));
        let stop_c = stop.clone();
        let handle = thread::spawn(move || {
            while !stop_c.load(Ordering::Relaxed) {
                match listener.accept() {
                    Ok((mut stream, _)) => {
                        let _ = stream.set_nonblocking(false);
                        let mut buf = vec![0u8; 8192];
                        let n = stream.read(&mut buf).unwrap_or(0);
                        let req = String::from_utf8_lossy(&buf[..n]).to_string();
                        let body = req.split("\r\n\r\n").nth(1).unwrap_or("").to_string();
                        let resp = echo_json_rpc_id(&body, handler(&body));
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
                        thread::sleep(Duration::from_millis(20));
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

fn echo_json_rpc_id(request_body: &str, response_body: String) -> String {
    let Ok(request) = serde_json::from_str::<serde_json::Value>(request_body) else {
        return response_body;
    };
    let Some(request_id) = request.get("id").cloned() else {
        return response_body;
    };
    let Ok(mut response) = serde_json::from_str::<serde_json::Value>(&response_body) else {
        return response_body;
    };
    if response.get("id").is_none() {
        return response_body;
    }
    response["id"] = request_id;
    response.to_string()
}

impl Drop for FakeRpcServer {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
    }
}
