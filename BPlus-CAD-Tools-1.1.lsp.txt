;;; BPlus CAD Tools 1.1 - Windows AutoCAD 2021 target, also plain CAD in Civil 3D.
;;; ASCII source for legacy AutoLISP encoding. All coordinates are WCS, E=X,N=Y.
;;; No CRS/unit transformation. Static labels, not associative fields.
;;; Test on a COPY of a drawing. Commands: BP BPSET BPLEN BPLL BPAL BPCO
;;; BPIN BPOUT BPCONV BPVERT BPSLOPE BPHELP
(vl-load-com)
(if (not *bp:prec*) (setq *bp:prec* 3))
(if (not *bp:height*) (setq *bp:height* (getvar "TEXTSIZE")))
(if (<= *bp:height* 0.0) (setq *bp:height* 2.5))

(defun bp:doc () (vla-get-ActiveDocument (vlax-get-acad-object)))
(defun bp:space ()
  (if (= 1 (getvar "CVPORT"))
    (vla-get-PaperSpace (bp:doc)) (vla-get-ModelSpace (bp:doc))))
(defun bp:fail (msg) (princ (strcat "\nBPlus: " msg)) (exit))
(defun bp:run (fn / *error* doc undo oldzin)
  (setq doc (bp:doc) undo nil oldzin (getvar "DIMZIN") *bp:io* nil *bp:temps* nil *bp:excelopts* nil)
  (defun *error* (msg)
    (bp:cleanup)
    (setvar "DIMZIN" oldzin)
    (if undo (vla-EndUndoMark doc))
    (if (and msg (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*")))
      (princ (strcat "\nBPlus error: " msg)))
    (princ "\nStopped. If objects were added, use U once to undo this command.")
    (princ))
  (setvar "DIMZIN" 0)
  (vla-StartUndoMark doc) (setq undo T)
  (apply fn nil)
  (bp:cleanup)
  (vla-EndUndoMark doc) (setq undo nil)
  (setvar "DIMZIN" oldzin) (princ))
(defun bp:num (v) (rtos v 2 *bp:prec*))
(defun bp:xyz (p) (list (car p) (cadr p) (if (caddr p) (caddr p) 0.0)))
(defun bp:wcs (p) (bp:xyz (trans p 1 0)))
(defun bp:point (prompt / p) (if (setq p (getpoint prompt)) (bp:wcs p) (bp:fail "Cancelled.")))
(defun bp:variant (xs / a)
  (setq a (vlax-make-safearray vlax-vbDouble (cons 0 (1- (length xs)))))
  (vlax-safearray-fill a xs) a)
(defun bp:leader (p q text / obj)
  (setq obj (vla-AddMLeader (bp:space) (bp:variant (append p q)) 0))
  (vla-put-ContentType obj 2)
  (vla-put-TextString obj text)
  (vla-put-TextHeight obj *bp:height*) obj)
(defun bp:mtext (p text / obj)
  (setq obj (vla-AddMText (bp:space) (vlax-3d-point p) (* 35.0 *bp:height*) text))
  (vla-put-Height obj *bp:height*) obj)
(defun bp:coords (p)
  (strcat "E = " (bp:num (car p)) "\\PN = " (bp:num (cadr p))
          "\\PZ = " (bp:num (caddr p))))
(defun bp:yes (prompt / a) (initget "Yes No") (setq a (getkword prompt)) (= a "Yes"))
(defun bp:selectcurve (/ s e typ flags)
  (setq s (entsel "\nSelect a polyline: "))
  (if (not s) (bp:fail "No selection."))
  (setq e (car s) typ (cdr (assoc 0 (entget e))) flags (cdr (assoc 70 (entget e))))
  (if (or (not (member typ '("LWPOLYLINE" "POLYLINE")))
          (and (= typ "POLYLINE") (/= 0 (logand flags 80))))
    (bp:fail "Only ordinary 2D/3D polylines, not meshes/polyfaces.")) e)
(defun bp:curvelen (e / result)
  (setq result (vl-catch-all-apply 'vlax-curve-getDistAtParam
    (list e (vlax-curve-getEndParam e))))
  (if (or (vl-catch-all-error-p result) (not (numberp result)))
    (bp:fail "Cannot measure this curve.")) result)

(defun bp:set (/ n h)
  (initget 4) (setq n (getint (strcat "\nDecimal places 0..8 <" (itoa *bp:prec*) ">: ")))
  (if n (if (<= n 8) (setq *bp:prec* n) (bp:fail "Precision must be 0..8.")))
  (initget 6) (setq h (getreal (strcat "\nText height in drawing units <" (bp:num *bp:height*) ">: ")))
  (if h (setq *bp:height* h))
  (princ "\nSettings updated. No unit conversion is applied."))
(defun bp:len (/ ss i e flags total count skipped p)
  (princ "\nSelect polylines. Length includes arcs and 3D slope length.")
  (setq ss (ssget '((0 . "LWPOLYLINE,POLYLINE"))) total 0.0 count 0 skipped 0 i 0)
  (if (not ss) (bp:fail "No polylines selected."))
  (repeat (sslength ss)
    (setq e (ssname ss i) i (1+ i) flags (cdr (assoc 70 (entget e))))
    (if (= 0 (logand flags 80))
      (setq total (+ total (bp:curvelen e)) count (1+ count))
      (setq skipped (1+ skipped))))
  (princ (strcat "\nPolylines: " (itoa count) "; skipped mesh/polyface: " (itoa skipped)
    "; total length = " (bp:num total) " drawing units."))
  (if (and (> count 0) (bp:yes "\nPlace total as text? [Yes/No] <No>: "))
    (progn (setq p (bp:point "\nText location: "))
      (bp:mtext p (strcat "Total L = " (bp:num total) "\\PCount = " (itoa count))))))
(defun bp:ll (/ e p q)
  (setq e (bp:selectcurve) p (bp:point "\nArrow tip on object: ")
    q (bp:point "\nLeader text location: "))
  (bp:leader p q (strcat "L = " (bp:num (bp:curvelen e))))
  (princ "\nStatic length label added. Redo the label after editing the polyline."))
(defun bp:al (/ e a p q)
  (setq e (bp:selectcurve))
  (if (not (vlax-curve-isClosed e)) (bp:fail "Area requires a closed 2D polyline."))
  (if (and (= "POLYLINE" (cdr (assoc 0 (entget e))))
           (/= 0 (logand 8 (cdr (assoc 70 (entget e))))))
    (bp:fail "3D polylines are not accepted for area."))
  (setq a (vl-catch-all-apply 'vlax-curve-getArea (list e)))
  (if (or (vl-catch-all-error-p a) (not (numberp a))) (bp:fail "Cannot measure area."))
  (setq p (bp:point "\nArrow tip: ") q (bp:point "\nLeader text location: "))
  (bp:leader p q (strcat "A = " (bp:num (abs a))))
  (princ "\nStatic area in square drawing units. Check boundary for self-intersections."))
(defun bp:co (/ p q)
  (while (setq p (getpoint "\nCoordinate point, use object snap <finish>: "))
    (setq p (bp:wcs p) q (bp:point "\nLeader text location: "))
    (bp:leader p q (bp:coords p))))
(defun bp:slope (/ p q dx dy dz hd sd)
  (setq p (bp:point "\nFirst point: ") q (bp:point "\nSecond point: ")
    dx (- (car q) (car p)) dy (- (cadr q) (cadr p)) dz (- (caddr q) (caddr p))
    hd (sqrt (+ (* dx dx) (* dy dy))) sd (distance p q))
  (princ (strcat "\nHorizontal = " (bp:num hd) "; slope distance = " (bp:num sd)
    "; delta Z = " (bp:num dz)))
  (if (> hd 1e-12) (princ (strcat "; slope = " (bp:num (* 100.0 (/ dz hd))) "%"))
    (princ "; slope undefined (vertical/coincident points).")))

;;; Survey file layer. No evaluation of file text. Dot decimals only.
;;; Row internal representation: (point-id E N Z description).
(defun bp:trim (s) (vl-string-trim (strcat " " (chr 9) (chr 13) (chr 10)) s))
(defun bp:digits (s / i c count dot ok)
  (setq s (bp:trim s) i 1 count 0 dot nil ok T)
  (if (member (substr s 1 1) '("+" "-")) (setq i 2))
  (while (<= i (strlen s))
    (setq c (ascii (substr s i 1)) i (1+ i))
    (cond ((and (>= c 48) (<= c 57)) (setq count (1+ count)))
          ((and (= c 46) (not dot)) (setq dot T))
          (T (setq ok nil))))
  (and ok (> count 0)))
(defun bp:chars (s / i out)
  (setq i 1 out nil)
  (repeat (strlen s) (setq out (cons (substr s i 1) out) i (1+ i))) (reverse out))
(defun bp:validfmt (s / cs ok c)
  (setq cs (bp:chars s) ok (and (member (strlen s) '(3 4 5)) T))
  (foreach c cs
    (if (or (not (member c '("P" "E" "N" "Z" "D")))
            (/= 1 (length (vl-remove-if-not '(lambda (x) (= c x)) cs)))) (setq ok nil)))
  (and ok (member "E" cs) (member "N" cs) (member "Z" cs) T))
(defun bp:format (prompt / s)
  (setq s (strcase (bp:trim (getstring T prompt))))
  (if (= s "") (setq s "PNEZD"))
  (if (not (bp:validfmt s)) (bp:fail "Use E,N,Z once each; optional P and D once. Example PNEZD or PENZD or ENZ.")) s)
(defun bp:delimiter (prompt / s)
  (initget "Comma Semicolon Tab Space") (setq s (getkword prompt))
  (cond ((= s "Semicolon") ";") ((= s "Tab") (chr 9)) ((= s "Space") " ") (T ",")))
;;; Split quoted CSV/TXT; supports escaped quotes and rejects malformed quoting.
;;; Space mode collapses whitespace. Other delimiters preserve empty fields.
(defun bp:split (s sep / i n c next state field fields err ws)
  (setq i 1 n (strlen s) state 0 field "" fields nil err nil ws (= sep " "))
  (while (and (<= i n) (not err))
    (setq c (substr s i 1) next (if (< i n) (substr s (1+ i) 1) ""))
    (cond
      ((= state 2)
        (if (= c "\"")
          (if (= next "\"") (setq field (strcat field "\"") i (1+ i)) (setq state 3))
          (setq field (strcat field c))))
      ((or (= c sep) (and ws (= c (chr 9))))
        (if (or (not ws) (/= state 0)) (setq fields (cons (bp:trim field) fields)))
        (setq field "" state 0))
      ((= state 3) (if (not (member c (list " " (chr 9)))) (setq err "Text after closing quote.")))
      ((= c "\"") (if (= (bp:trim field) "") (setq field "" state 2) (setq err "Quote inside unquoted field.")))
      (T (setq field (strcat field c) state 1)))
    (setq i (1+ i)))
  (if (= state 2) (setq err "Unclosed quote."))
  (if err (list nil err)
    (progn
      (if (or (not ws) (/= state 0)) (setq fields (cons (bp:trim field) fields)))
      (list T (reverse fields)))))
(defun bp:row (fields fmt serial / pairs i c e n z id desc)
  (if (/= (length fields) (strlen fmt)) (list nil "Wrong column count.")
    (progn
      (setq pairs nil i 0)
      (foreach c (bp:chars fmt) (setq pairs (cons (cons c (nth i fields)) pairs) i (1+ i)))
      (setq e (cdr (assoc "E" pairs)) n (cdr (assoc "N" pairs)) z (cdr (assoc "Z" pairs))
        id (if (assoc "P" pairs) (cdr (assoc "P" pairs)) (itoa serial))
        desc (if (assoc "D" pairs) (cdr (assoc "D" pairs)) ""))
      (cond ((= id "") (list nil "Blank point ID."))
        ((or (not (bp:digits e)) (not (bp:digits n)) (not (bp:digits z)))
          (list nil "Invalid coordinate. Use dot decimals, no thousands separators or exponents."))
        (T (list T (list id (atof e) (atof n) (atof z) desc)))))))
(defun bp:readrows (path fmt sep header / line ln serial parsed rec rows bad ids sorted prev)
  (setq *bp:io* (open path "r"))
  (if (not *bp:io*) (bp:fail "Cannot read input file."))
  (setq ln 0 serial 0 rows nil bad nil)
  (while (and (setq line (read-line *bp:io*)) (not bad))
    (setq ln (1+ ln))
    (if (/= "" (bp:trim line))
      (if header (setq header nil)
        (progn
          (setq serial (1+ serial) parsed (bp:split line sep))
          (if (car parsed) (setq rec (bp:row (cadr parsed) fmt serial)) (setq rec parsed))
          (if (car rec) (setq rows (cons (cadr rec) rows))
            (setq bad (strcat "Line " (itoa ln) ": " (cadr rec))))
          (if (> serial 20000) (setq bad "Maximum 20000 points per operation."))))))
  (close *bp:io*) (setq *bp:io* nil)
  (if bad (bp:fail (strcat bad " No output was written.")))
  (if (not rows) (bp:fail "No data rows."))
  ;; vl-sort can remove duplicates; compare before/after length to detect them.
  (setq ids (mapcar 'car rows) sorted (vl-sort ids '<))
  (if (/= (length ids) (length sorted)) (bp:fail "Duplicate point IDs. Make them unique before use."))
  (reverse rows))
(defun bp:read-delimited (/ path fmt sep header)
  (princ "\nWCS only: E=X, N=Y, Z=elevation. No CRS or unit conversion.")
  (setq path (getfiled "Input point file" "" "csv;txt;asc" 0))
  (if (not path) (bp:fail "Cancelled."))
  (setq fmt (bp:format "\nINPUT columns, e.g. PNEZD / PENZD / ENZ <PNEZD>: ")
    sep (bp:delimiter "\nINPUT separator [Comma/Semicolon/Tab/Space] <Comma>: ")
    header (bp:yes "\nSkip first nonblank line as header? [Yes/No] <No>: "))
  (bp:readrows path fmt sep header))
(defun bp:preview (rows / r)
  (setq r (car rows))
  (princ (strcat "\nValid points: " (itoa (length rows)) ". FIRST point: " (car r)
    " E=" (rtos (nth 1 r) 2 6) " N=" (rtos (nth 2 r) 2 6) " Z=" (rtos (nth 3 r) 2 6)))
  (princ "\nCheck E/N order, units, CRS and known control points BEFORE using data."))
(defun bp:quote (s / i out c)
  (setq i 1 out "\"")
  (repeat (strlen s) (setq c (substr s i 1) i (1+ i)
    out (strcat out (if (= c "\"") "\"\"" c))))
  (strcat out "\""))
(defun bp:encode (r fmt sep prec / out c v)
  (setq out "")
  (foreach c (bp:chars fmt)
    (setq v (cond ((= c "P") (bp:quote (car r)))
      ((= c "D") (bp:quote (nth 4 r)))
      ((= c "E") (rtos (nth 1 r) 2 prec))
      ((= c "N") (rtos (nth 2 r) 2 prec))
      (T (rtos (nth 3 r) 2 prec))))
    (setq out (if (= out "") v (strcat out sep v)))) out)
(defun bp:write-delimited (rows / fmt sep path r prec header c)
  (bp:preview rows)
  (setq fmt (bp:format "\nOUTPUT columns, e.g. PNEZD / PENZD / ENZ <PNEZD>: ")
    sep (bp:delimiter "\nOUTPUT separator [Comma/Semicolon/Tab/Space] <Comma>: "))
  (initget 4) (setq prec (getint "\nOUTPUT decimal places 0..8 <6>: "))
  (if (not prec) (setq prec 6))
  (if (> prec 8) (bp:fail "Precision must be 0..8."))
  (setq header (bp:yes "\nWrite column header? [Yes/No] <No>: "))
  (princ "\nP and D text is quoted. Confirm your instrument accepts quoted text.")
  (setq path (getfiled "Save NEW generic point file" "BPlus-points.csv" "csv;txt;asc" 1))
  (if (not path) (bp:fail "Cancelled."))
  (if (findfile path) (bp:fail "File already exists. Choose a NEW name; overwriting is disabled."))
  (if (not (bp:yes "\nConfirm output order and precision. Write file? [Yes/No] <No>: "))
    (bp:fail "Cancelled. No output written."))
  (setq *bp:io* (open path "w"))
  (if (not *bp:io*) (bp:fail "Cannot create output file."))
  (if header
    (progn
      (setq r "") (foreach c (bp:chars fmt) (setq r (if (= r "") c (strcat r sep c))))
      (write-line r *bp:io*)))
  (foreach r rows (write-line (bp:encode r fmt sep prec) *bp:io*))
  (close *bp:io*) (setq *bp:io* nil)
  (princ (strcat "\nWrote " (itoa (length rows)) " points to " path))
  (princ "\nGeneric delimited text only. Verify a few control points in device software."))

(defun bp:out (/ ss i e p xd id desc rows)
  (bp:model)
  (princ "\nSelect AutoCAD POINT entities only (not COGO points or blocks).")
  (setq ss (ssget '((0 . "POINT"))) i 0 rows nil)
  (if (not ss) (bp:fail "No POINT entities selected."))
  (repeat (sslength ss)
    (setq e (entget (ssname ss i) '("BPLUSPTS")) p (cdr (assoc 10 e))
      xd (cdr (cadr (assoc -3 e)))
      id (if xd (cdr (car xd)) (itoa (1+ i)))
      desc (if xd (cdr (cadr xd)) "")
      rows (cons (list id (car p) (cadr p) (caddr p) desc) rows) i (1+ i)))
  (princ "\nIDs are sequential for ordinary points; imported BPlus IDs are retained.")
  (bp:writejob (reverse rows)))
(defun bp:conv (/ rows) (setq rows (bp:readjob)) (bp:writejob rows))
(defun bp:vert (/ e typ flags end i p rows)
  (bp:model)
  (setq e (bp:selectcurve) typ (cdr (assoc 0 (entget e)))
    flags (cdr (assoc 70 (entget e))))
  (if (and (= typ "POLYLINE") (/= 0 (logand flags 6)))
    (bp:fail "Curve-fit/spline-fit legacy polylines not supported for vertex export."))
  (setq end (fix (vlax-curve-getEndParam e)) i 0 rows nil)
  (if (vlax-curve-isClosed e) (setq end (1- end)))
  (while (<= i end)
    (setq p (vlax-curve-getPointAtParam e i)
      rows (cons (list (itoa (1+ i)) (car p) (cadr p) (caddr p) "VERTEX") rows) i (1+ i)))
  (princ "\nExports original vertices only. Arc interiors are NOT densified.")
  (bp:writejob (reverse rows)))


(defun c:BP () (bp:run 'bp:menu))
(defun c:BPSET () (bp:run 'bp:set))
(defun c:BPLEN () (bp:run 'bp:len))
(defun c:BPLL () (bp:run 'bp:ll))
(defun c:BPAL () (bp:run 'bp:al))
(defun c:BPCO () (bp:run 'bp:co))
(defun c:BPIN () (bp:run 'bp:in))
(defun c:BPOUT () (bp:run 'bp:out))
(defun c:BPCONV () (bp:run 'bp:conv))
(defun c:BPVERT () (bp:run 'bp:vert))
(defun c:BPSLOPE () (bp:run 'bp:slope))
(defun c:BPHELP () (bp:help) (princ))



;;; Extended local survey/Google Earth formats. Embedded PowerShell helper is
;;; written to a unique temp file, invoked synchronously, then removed.
;;; It does local file/XML/ZIP/math only, never downloads linked KML content.
(defun bp:ascii (s / ok c)
  (setq ok T)
  (foreach c (vl-string->list s) (if (or (< c 32) (> c 126)) (setq ok nil))) ok)
(defun bp:dupsp (ids / order prev value duplicate)
  (setq order (vl-sort-i ids '<) prev nil duplicate nil)
  (while (and order (not duplicate))
    (setq value (nth (car order) ids) order (cdr order))
    (if (equal value prev) (setq duplicate T))
    (setq prev value)) duplicate)
(defun bp:model ()
  (if (= 1 (getvar "CVPORT"))
    (bp:fail "Survey drawing commands require Model space. Activate Model or a model viewport first.")))
(defun bp:checkrows (rows / ids r)
  (if (or (not rows) (> (length rows) 20000)) (bp:fail "Need 1..20000 points."))
  (foreach r rows
    (if (or (= (car r) "") (> (strlen (car r)) 200) (> (strlen (nth 4 r)) 200)
            (not (bp:ascii (car r))) (not (bp:ascii (nth 4 r))))
      (bp:fail "Point ID/description must be printable ASCII, at most 200 characters.")))
  (setq ids (mapcar 'car rows))
  (if (bp:dupsp ids)
    (bp:fail "Duplicate point IDs. Make IDs unique, or export/import separately.")) rows)
(defun bp:cleanup (/ p)
  (if *bp:io* (progn (close *bp:io*) (setq *bp:io* nil)))
  (foreach p *bp:temps* (if (findfile p) (vl-file-delete p)))
  (setq *bp:temps* nil *bp:excelopts* nil))
(defun bp:temp (ext / p)
  (setq p (vl-filename-mktemp "BPlus-" nil ext))
  ;; mktemp may reserve a file. The helper never overwrites targets.
  (if (findfile p) (vl-file-delete p))
  (setq *bp:temps* (cons p *bp:temps*)) p)
(defun bp:arg (s)
  (if (or (vl-string-search "%" s) (vl-string-search "\"" s) (vl-string-search (chr 10) s) (vl-string-search (chr 13) s))
    (bp:fail "Invalid command argument."))
  (strcat "\"" s "\""))
(defun bp:crs (forimport / cs z h al)
  (princ (if forimport "\nChoose TARGET drawing CRS." "\nChoose SOURCE drawing CRS."))
  (princ "\nNo automatic CAD/Map CRS detection. WGS84 only; meters for UTM and Z.")
  (initget "UTM LonLat")
  (setq cs (getkword (strcat "\nCRS [UTM/LonLat] <" (if *bp:crs* *bp:crs* "choose explicitly") ">: ")))
  (if (not cs) (setq cs *bp:crs*))
  (if (not cs) (bp:fail "Choose a CRS explicitly."))
  (setq z 0 h "N")
  (if (= cs "UTM")
    (progn
      (initget 6) (setq z (getint (strcat "\nWGS84 UTM zone 1..60" (if *bp:zone* (strcat " <" (itoa *bp:zone*) ">") "") ": ")))
      (if (not z) (setq z *bp:zone*))
      (if (or (not z) (< z 1) (> z 60)) (bp:fail "UTM zone must be 1..60."))
      (initget "North South")
      (setq h (getkword (strcat "\nHemisphere [North/South] <" (if *bp:hemi* *bp:hemi* "choose explicitly") ">: ")))
      (if (not h) (setq h *bp:hemi*))
      (if (not h) (bp:fail "Choose hemisphere explicitly."))
      (setq *bp:zone* z *bp:hemi* h h (if (= h "South") "S" "N")))
    (princ "\nLonLat means drawing X=longitude, Y=latitude in decimal degrees, Z in meters."))
  (if forimport
    (progn
      (initget "Zero Absolute")
      (setq al (getkword "\nImport altitude [Zero/Absolute] <Zero>: "))
      (if (not al) (setq al "Zero"))
      (if (= al "Zero") (princ "\nAll imported Z will be ZERO, Google Earth terrain heights are NOT survey elevations.")))
    (progn
      (initget "Clamp Absolute")
      (setq al (getkword "\nGoogle Earth altitude [Clamp/Absolute] <Clamp>: "))
      (if (not al) (setq al "Clamp"))
      (if (= al "Absolute") (princ "\nAbsolute assumes Z in meters relative to sea level. NO geoid/datum conversion."))))
  (princ (strcat "\nCHOSEN: WGS84 " cs (if (= cs "UTM") (strcat " zone " (itoa z) h) " X=lon,Y=lat") "; altitude=" al))
  (if (not (bp:yes "\nIs this correct for THIS drawing? [Yes/No] <No>: ")) (bp:fail "CRS not confirmed."))
  (setq *bp:crs* cs)
  (list cs z h al))
(defun bp:helper (mode source target fmt crs / script status shell exe cmd result msg line)
  (if (not crs) (setq crs '("None" 0 "N" "Zero")))
  (setq script (bp:temp ".ps1") status (bp:temp ".status.txt"))
  (setq *bp:io* (open script "w"))
  (if (not *bp:io*) (bp:fail "Cannot create temporary helper."))
  (foreach line *bp:ps-lines* (write-line line *bp:io*))
  (close *bp:io*) (setq *bp:io* nil)
  (setq exe (strcat (getenv "WINDIR") "\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"))
  (if (not (findfile exe)) (bp:fail "Windows PowerShell not found. CSV/TXT and drawing commands still work."))
  (setq cmd (strcat (bp:arg exe) " -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "
    (bp:arg script) " -Mode " mode " -Source " (bp:arg source) " -Target " (bp:arg target)
    " -Format " fmt " -Crs " (car crs) " -Zone " (itoa (nth 1 crs))
    " -Hemi " (nth 2 crs) " -Altitude " (nth 3 crs) " -Status " (bp:arg status)
    (if *bp:excelopts* (strcat " -Sheet " (itoa (car *bp:excelopts*))
      " -Columns " (bp:arg (cadr *bp:excelopts*)) " -SkipHeader " (caddr *bp:excelopts*)) "")))
  (princ "\nProcessing locally with Windows PowerShell; no network access is requested...")
  (setq shell (vlax-create-object "WScript.Shell"))
  (setq result (vl-catch-all-apply 'vlax-invoke-method (list shell 'Run cmd 0 :vlax-true)))
  (vlax-release-object shell)
  (if (vl-catch-all-error-p result) (bp:fail "Could not run local helper (Windows policy/permissions)."))
  (setq msg "Local helper failed. Check Windows PowerShell policy/permissions.")
  (if (findfile status)
    (progn (setq *bp:io* (open status "r"))
      (if *bp:io* (setq msg (read-line *bp:io*)))
      (if *bp:io* (close *bp:io*)) (setq *bp:io* nil)))
  (if (or (/= result 0) (not msg) (/= (substr msg 1 3) "OK "))
    (bp:fail (if msg msg "Empty helper result.")))
  (princ (strcat "\n" msg)) T)
(defun bp:bridge (rows / p r)
  (bp:checkrows rows)
  (setq p (bp:temp ".csv") *bp:io* (open p "w"))
  (if (not *bp:io*) (bp:fail "Cannot create local intermediate file."))
  (foreach r rows (write-line (bp:encode r "PENZD" "," 8) *bp:io*))
  (close *bp:io*) (setq *bp:io* nil) p)
(defun bp:readjob (/ kind path crs bridge)
  (initget "Delimited GSI SDR KML KMZ Excel")
  (setq kind (getkword "\nINPUT type [Delimited/GSI/SDR/KML/KMZ/Excel] <Delimited>: "))
  (if (= kind "Excel") (bp:excelread)
  (if (or (not kind) (= kind "Delimited")) (bp:checkrows (bp:read-delimited))
    (progn
      (setq path (getfiled "Input survey / Google Earth file" "" (strcase kind T) 0))
      (if (not path) (bp:fail "Cancelled."))
      (setq crs nil)
      (if (member kind '("KML" "KMZ")) (setq crs (bp:crs T))
        (if (not (bp:yes "\nGSI/SDR coordinates are METERS. Drawing also uses meters? [Yes/No] <No>: "))
          (bp:fail "No unit conversion. Use a meter-based drawing.")))
      (setq bridge (bp:temp ".csv"))
      (bp:helper "Read" path bridge kind crs)
      (bp:checkrows (bp:readrows bridge "PENZD" "," nil))))))
(defun bp:writejob (rows / kind path crs bridge ext)
  (bp:checkrows rows)
  (initget "Delimited GSI8 GSI16 SDR KML KMZ")
  (setq kind (getkword "\nOUTPUT type [Delimited/GSI8/GSI16/SDR/KML/KMZ] <Delimited>: "))
  (if (or (not kind) (= kind "Delimited")) (bp:write-delimited rows)
    (progn
      (bp:preview rows) (setq crs nil)
      (if (member kind '("KML" "KMZ")) (setq crs (bp:crs nil))
        (progn
          (princ "\nGSI output: millimeters (0.001 m), SDR33 output: 0.0001 m. IDs/codes have strict widths.")
          (if (not (bp:yes "\nInput drawing/file coordinates are METERS? [Yes/No] <No>: "))
            (bp:fail "No unit conversion. Use meter coordinates."))))
      (setq ext (if (member kind '("GSI8" "GSI16")) "gsi" (strcase kind T))
        path (getfiled "Save NEW output file" (strcat "BPlus-export." ext) ext 1))
      (if (not path) (bp:fail "Cancelled."))
      (if (findfile path) (bp:fail "Output exists. Choose a new filename."))
      (if (not (bp:yes "\nWrite this file? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
      (setq bridge (bp:bridge rows))
      (bp:helper "Write" bridge path kind crs)
      (princ (strcat "\nSaved " path " . Check in instrument/Google Earth before use.")))))
(defun bp:importrows (rows / r obj en count)
  (setq rows (bp:checkrows rows)) (bp:preview rows)
  (if (not (bp:yes "\nCreate these as AutoCAD POINT entities? [Yes/No] <No>: "))
    (bp:fail "Cancelled. Drawing unchanged."))
  (regapp "BPLUSPTS") (setq count 0)
  (foreach r rows
    (setq obj (vla-AddPoint (bp:space) (vlax-3d-point (list (nth 1 r) (nth 2 r) (nth 3 r))))
      en (vlax-vla-object->ename obj))
    (if (not (entmod (append (entget en)
      (list (list -3 (list "BPLUSPTS" (cons 1000 (car r)) (cons 1000 (nth 4 r))))))))
      (bp:fail "Could not store point metadata. Use U to undo partial import."))
    (setq count (1+ count)))
  (princ (strcat "\nImported " (itoa count) " AutoCAD POINT entities, not Civil 3D COGO points."))
  (princ "\nIDs/descriptions retained as XData. Use PTYPE if markers are hard to see."))
;;; KML/KMZ geometry export preserves vertices and subdivides curved segments.
(defun bp:geout (/ ss fmt crs path bridge step i e typ flags len j end par a b count p rows geom tag r points)
  (bp:model)
  (initget "KML KMZ") (setq fmt (getkword "\nOutput [KML/KMZ] <KMZ>: "))
  (if (not fmt) (setq fmt "KMZ"))
  (setq crs (bp:crs nil))
  (princ "\nSelect POINTs and ordinary 2D/3D polylines (not COGO points).")
  (setq ss (ssget '((0 . "POINT,LWPOLYLINE,POLYLINE"))))
  (if (not ss) (bp:fail "No geometry selected."))
  (princ "\nPolylines become LineStrings, including closed ones. Not filled polygons.")
  (initget 6)
  (setq step (getreal (if (= (car crs) "UTM")
    "\nMaximum sample spacing in meters <5>: " "\nMaximum sample spacing in degrees <0.00005>: ")))
  (if (not step) (setq step (if (= (car crs) "UTM") 5.0 0.00005)))
  (setq i 0 count 0 rows nil geom nil)
  (repeat (sslength ss)
    (setq e (ssname ss i) i (1+ i) typ (cdr (assoc 0 (entget e)))
      tag (strcat "CAD-" (cdr (assoc 5 (entget e)))))
    (if (= typ "POINT")
      (progn (setq p (cdr (assoc 10 (entget e))) r (list tag (car p) (cadr p) (caddr p) ""))
        (setq rows (cons (append r (list tag "Point")) rows) count (1+ count)))
      (progn
        (setq flags (cdr (assoc 70 (entget e))))
        (if (/= 0 (logand flags 86)) (bp:fail "Meshes/polyfaces and fitted legacy polylines are not supported."))
        (setq end (fix (vlax-curve-getEndParam e)) j 0 points nil)
        ;; Sample EACH segment separately, preserving all original vertices.
        (while (< j end)
          (setq a (vlax-curve-getDistAtParam e j) b (vlax-curve-getDistAtParam e (1+ j))
            len (- b a) par a)
          (while (< par b)
            (setq p (vlax-curve-getPointAtDist e par) points (cons p points) par (+ par step)
              count (1+ count))
            (if (> count 20000) (bp:fail "More than 20000 samples. Increase spacing or select less.")))
          (setq j (1+ j)))
        (setq points (reverse (cons (vlax-curve-getEndPoint e) points)) j 0 count (1+ count))
        (foreach p points
          (setq j (1+ j) r (list (strcat tag "-" (itoa j)) (car p) (cadr p) (caddr p) ""))
          (setq rows (cons (append r (list tag "Line")) rows))))))
  (if (> count 20000) (bp:fail "Maximum 20000 samples per operation."))
  (setq rows (reverse rows)) (bp:checkrows rows)
  (setq path (getfiled "Save NEW Google Earth file" (strcat "BPlus-drawing." (strcase fmt T)) (strcase fmt T) 1))
  (if (not path) (bp:fail "Cancelled."))
  (if (findfile path) (bp:fail "Output exists. Choose a new filename."))
  (princ (strcat "\nGeometry samples: " (itoa (length rows))))
  (if (not (bp:yes "\nWrite geometry using the confirmed CRS? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (setq bridge (bp:temp ".csv") *bp:io* (open bridge "w"))
  (if (not *bp:io*) (bp:fail "Cannot create intermediate file."))
  (foreach r rows
    (write-line (strcat (bp:encode r "PENZD" "," 8) "," (bp:quote (nth 5 r)) "," (bp:quote (nth 6 r))) *bp:io*))
  (close *bp:io*) (setq *bp:io* nil)
  (bp:helper "GeoWrite" bridge path fmt crs)
  (princ (strcat "\nSaved " path ". Curves are sampled approximations. Check in Google Earth.")))
(defun bp:numlabels (/ prefix n inc p obj)
  (setq prefix (getstring T "\nPrefix <none>: "))
  (setq n (getint "\nStarting integer <1>: ")) (if (not n) (setq n 1))
  (initget 6) (setq inc (getint "\nPositive increment <1>: ")) (if (not inc) (setq inc 1))
  (while (setq p (getpoint "\nNumber label location <finish>: "))
    (setq obj (vla-AddText (bp:space) (strcat prefix (itoa n)) (vlax-3d-point (bp:wcs p)) *bp:height*) n (+ n inc))))
(defun bp:count (/ ss i obj name counts item text p)
  (setq ss (ssget '((0 . "INSERT"))) i 0 counts nil)
  (if (not ss) (bp:fail "No block references selected."))
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i)
      name (if (vlax-property-available-p obj 'EffectiveName) (vla-get-EffectiveName obj) (vla-get-Name obj))
      item (assoc name counts))
    (if item (setq counts (subst (cons name (1+ (cdr item))) item counts))
      (setq counts (cons (cons name 1) counts))))
  (setq text "Block reference count (top-level only)")
  (foreach item (vl-sort counts '(lambda (a b) (< (car a) (car b))))
    (princ (strcat "\n" (car item) " = " (itoa (cdr item))))
    (setq text (strcat text "\\P" (car item) " = " (itoa (cdr item)))))
  (princ "\nNested blocks not expanded. MINSERT arrays count as one selected reference.")
  (if (bp:yes "\nPlace summary as text? [Yes/No] <No>: ")
    (progn (setq p (bp:point "\nSummary location: ")) (bp:mtext p text))))
(defun bp:textsum (/ ss i s total count skipped obj)
  (setq ss (ssget '((0 . "TEXT,MTEXT"))) i 0 total 0.0 count 0 skipped 0)
  (if (not ss) (bp:fail "No text selected."))
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i) s (bp:trim (vla-get-TextString obj)))
    (if (bp:digits s) (setq total (+ total (atof s)) count (1+ count)) (setq skipped (1+ skipped))))
  (princ (strcat "\nSum = " (bp:num total) "; numeric items = " (itoa count) "; skipped = " (itoa skipped)))
  (princ "\nOnly plain signed dot-decimal text. Units, fields and MTEXT format codes are skipped."))
(defun bp:help (/ section pair)
  (princ "\nBPlus CAD Tools 1.1 TRIAL - Full Windows AutoCAD 2021 target. CAD/UI runtime untested.")
  (foreach section *bp:ui-groups*
    (princ (strcat "\n--- " (car section) " ---"))
    (foreach pair (cdr section) (princ (strcat "\n" (cadr pair) " : " (car pair)))))
  (princ "\nBPPANEL opens clickable tools. BPMENU restores top menu. APPLOAD Startup Suite can autoload.")
  (princ "\nStatic labels/tables; WCS E=X,N=Y; drawing units. Use copies and control points.")
  (princ "\nXLSX is read locally without Excel. Legacy XLS: Save As XLSX first. No formula-cell import.")
  (princ "\nGSI/SDR metric Cartesian coordinates only; KML requires explicit WGS84 CRS. No COGO/datum/geoid conversion."))
(defun bp:menu (/ a)
  (bp:help)
  (initget "Length Label Area Coordinates Import Export Convert Vertices Geo Number Blocks Sum Slope Settings Test Help")
  (setq a (getkword "\n[Length/Label/Area/Coordinates/Import/Export/Convert/Vertices/Geo/Number/Blocks/Sum/Slope/Settings/Test/Help] <Help>: "))
  (cond ((= a "Length") (bp:len)) ((= a "Label") (bp:ll)) ((= a "Area") (bp:al))
    ((= a "Coordinates") (bp:co)) ((= a "Import") (bp:in)) ((= a "Export") (bp:out))
    ((= a "Convert") (bp:conv)) ((= a "Vertices") (bp:vert)) ((= a "Geo") (bp:geout))
    ((= a "Number") (bp:numlabels)) ((= a "Blocks") (bp:count)) ((= a "Sum") (bp:textsum))
    ((= a "Slope") (bp:slope)) ((= a "Settings") (bp:set)) ((= a "Test") (bp:test)) (T (bp:help))))
(defun bp:test (/ ok)
  (setq ok (and
    (bp:digits "-123.456") (not (bp:digits "12.3.4")) (not (bp:digits "1e3"))
    (bp:validfmt "PNEZD") (bp:validfmt "ENZ") (not (bp:validfmt "PNNZ"))
    (equal (cadr (bp:split "1,2,3,4,\"edge,wall\"" ",")) '("1" "2" "3" "4" "edge,wall"))
    (not (car (bp:split "\"unclosed" ",")))
    (equal (cadr (bp:split "  1  2 3  4 \"edge wall\" " " ")) '("1" "2" "3" "4" "edge wall"))
    (equal (cadr (bp:row '("A" "100" "200" "3" "C") "PNEZD" 1)) '("A" 200.0 100.0 3.0 "C"))))
  (if ok (princ "\nPASS: basic parser/column tests only. This does NOT test CAD geometry, PowerShell or instruments.")
    (princ "\nFAIL: parser check. Stop and report this message.")))
(defun c:BPGEO () (bp:run 'bp:geout))
(defun c:BPNUM () (bp:run 'bp:numlabels))
(defun c:BPCOUNT () (bp:run 'bp:count))
(defun c:BPTSUM () (bp:run 'bp:textsum))
(defun c:BPTEST () (bp:test) (princ))

(defun bp:in () (bp:model) (bp:importrows (bp:readjob)))
(defun bp:editable (e / name)
  (setq name (cdr (assoc 8 (entget e))))
  (if (= (vla-get-Lock (vla-Item (vla-get-Layers (bp:doc)) name)) :vlax-true)
    (bp:fail (strcat "Locked layer in selection: " name ". Unlock/deselect it first. Nothing modified."))))
(defun bp:editss (ss / i)
  (setq i 0) (repeat (sslength ss) (bp:editable (ssname ss i)) (setq i (1+ i))))
;;; BPlus 1.1 additional CAD routines. Static checks only; CAD execution untested.
(defun bp:az (p q / dx dy a)
  (setq dx (- (car q) (car p)) dy (- (cadr q) (cadr p)))
  (if (< (+ (* dx dx) (* dy dy)) 1e-20) nil
    (progn (setq a (atan dx dy)) (if (< a 0.0) (setq a (+ a (* 2.0 pi)))) a)))
(defun bp:dms (a / secs dd mm ss)
  (setq secs (fix (+ 0.5 (* a (/ 648000.0 pi)))) secs (rem secs 1296000)
    dd (fix (/ secs 3600)) mm (fix (/ (rem secs 3600) 60)) ss (rem secs 60))
  (strcat (itoa dd) "d " (itoa mm) "m " (itoa ss) "s"))
(defun bp:bear (/ p q a h pos)
  (setq p (bp:point "\nFROM point: ") q (bp:point "\nTO point: ") a (bp:az p q))
  (if (not a) (bp:fail "Azimuth undefined for coincident/vertical points."))
  (setq h (distance (list (car p) (cadr p) 0.0) (list (car q) (cadr q) 0.0))
    pos (bp:point "\nLeader text location: "))
  (bp:leader p pos (strcat "HD = " (bp:num h) "\\PAz = " (bp:dms a)))
  (princ "\nWCS azimuth clockwise from North, horizontal drawing distance. Static label."))
(defun bp:level (/ p q)
  (while (setq p (getpoint "\nSpot elevation point <finish>: "))
    (setq p (bp:wcs p) q (bp:point "\nText location: "))
    (bp:leader p q (strcat "Z = " (bp:num (caddr p))))))
(defun bp:chain (/ e step start d len p obj n)
  (bp:model) (setq e (bp:selectcurve) len (bp:curvelen e))
  (initget 7) (setq step (getreal "\nChainage interval in drawing distance units: "))
  (setq start (getreal "\nStarting chainage value <0>: ")) (if (not start) (setq start 0.0))
  (if (> (/ len step) 2000) (bp:fail "More than 2000 labels. Increase interval."))
  (princ "\n3D polylines use slope length. Labels follow vertex order from curve start.")
  (if (not (bp:yes "\nAdd static chainage labels? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (setq d 0.0 n 0)
  (while (<= d (+ len 1e-9))
    (setq p (vlax-curve-getPointAtDist e (min d len))
      obj (vla-AddText (bp:space) (strcat "CH " (bp:num (+ start d))) (vlax-3d-point p) *bp:height*)
      n (1+ n) d (+ d step)))
  (princ (strcat "\nAdded " (itoa n) " labels. No ticks/alignment station equations; endpoint only if on interval.")))
(defun bp:points (/ ss i out p e)
  (setq ss (ssget '((0 . "POINT"))) i 0 out nil)
  (if (not ss) (bp:fail "Select AutoCAD POINT entities, not COGO points."))
  (if (> (sslength ss) 2000) (bp:fail "Maximum 2000 points for this operation."))
  (repeat (sslength ss)
    (setq e (ssname ss i) i (1+ i) p (cdr (assoc 10 (entget e))))
    (setq out (cons (list e p) out))) (reverse out))
(defun bp:setz (relative / pts val p obj)
  (bp:model) (setq pts (bp:points))
  (foreach p pts (bp:editable (car p)))
  (setq val (getreal (if relative "\nAdd delta Z to each selected POINT: " "\nSet all selected POINT elevations to Z: ")))
  (if (not val) (bp:fail "Cancelled."))
  (if (not (bp:yes "\nModify selected POINT elevations? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (foreach p pts
    (setq obj (vlax-ename->vla-object (car p)))
    (vla-put-Coordinates obj (vlax-3d-point (list (car (cadr p)) (cadr (cadr p))
      (if relative (+ (caddr (cadr p)) val) val)))))
  (princ "\nPOINT elevations updated. Existing labels are not updated. U undoes this command."))
(defun bp:table (title headers rows / pos table rr cc value row)
  (if (> (length rows) 2000) (bp:fail "Maximum 2000 table rows."))
  (setq pos (bp:point "\nTable insertion point: ")
    table (vla-AddTable (bp:space) (vlax-3d-point pos) (+ 2 (length rows))
      (length headers) (* 2.5 *bp:height*) (* 14.0 *bp:height*)))
  (vla-put-RegenerateTableSuppressed table :vlax-true)
  (vla-SetText table 0 0 title)
  (setq cc 0)
  (foreach value headers (vla-SetText table 1 cc value) (setq cc (1+ cc)))
  (setq rr 2)
  (foreach row rows
    (setq cc 0)
    (foreach value row (vla-SetText table rr cc value) (setq cc (1+ cc)))
    (setq rr (1+ rr)))
  (vla-put-RegenerateTableSuppressed table :vlax-false)
  (vla-Update table)
  (princ "\nStatic AutoCAD table added. Table style controls appearance; values do not auto-update."))
(defun bp:cotab (/ pts p i rows)
  (bp:model) (setq pts (bp:points) i 0 rows nil)
  (foreach p pts (setq i (1+ i) rows (cons (list (itoa i) (bp:num (car (cadr p)))
    (bp:num (cadr (cadr p))) (bp:num (caddr (cadr p)))) rows)))
  (princ "\nIDs in this table are sequential selection indices, not stored survey point IDs.")
  (bp:table "WCS coordinates (selection order)" '("Index" "E (X)" "N (Y)" "Z") (reverse rows)))
(defun bp:pltab (/ ss i e rows a flags)
  (setq ss (ssget '((0 . "LWPOLYLINE,POLYLINE"))) i 0 rows nil)
  (if (not ss) (bp:fail "No polylines selected."))
  (repeat (sslength ss)
    (setq e (ssname ss i) i (1+ i) flags (cdr (assoc 70 (entget e))) a "n/a")
    (if (/= 0 (logand flags 80)) (bp:fail "Meshes/polyfaces not supported."))
    (if (and (vlax-curve-isClosed e) (not (and (= "POLYLINE" (cdr (assoc 0 (entget e)))) (/= 0 (logand flags 8)))))
      (progn (setq a (vl-catch-all-apply 'vlax-curve-getArea (list e)))
        (setq a (if (numberp a) (bp:num (abs a)) "n/a"))))
    (setq rows (cons (list (cdr (assoc 5 (entget e))) (bp:num (bp:curvelen e)) a) rows)))
  (bp:table "Polyline quantities (drawing units / square units)" '("Handle" "Length" "Closed 2D area") (reverse rows)))
(defun bp:hatch (/ ss i obj a total rows)
  (setq ss (ssget '((0 . "HATCH"))) i 0 total 0.0 rows nil)
  (if (not ss) (bp:fail "No hatches selected."))
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i)
      a (vl-catch-all-apply 'vla-get-Area (list obj)))
    (if (not (numberp a)) (bp:fail "A hatch has no valid area. Fix/evaluate it in AutoCAD first."))
    (setq total (+ total (abs a)) rows (cons (list (vla-get-Handle obj) (bp:num (abs a))) rows)))
  (princ (strcat "\nHatch area total = " (bp:num total) " square drawing units. Overlaps are counted separately."))
  (if (bp:yes "\nPlace area schedule? [Yes/No] <No>: ")
    (bp:table "Hatch areas (overlaps counted separately)" '("Handle" "Area")
      (append (reverse rows) (list (list "TOTAL" (bp:num total)))))))
(defun bp:laypick (/ e)
  (setq e (car (entsel "\nPick an object on the target layer: ")))
  (if (not e) (bp:fail "No object picked.")) (cdr (assoc 8 (entget e))))
(defun bp:laycurrent (/ name obj)
  (setq name (bp:laypick) obj (vla-Item (vla-get-Layers (bp:doc)) name))
  (if (= (vla-get-Freeze obj) :vlax-true) (bp:fail "Layer is frozen. Thaw it first."))
  (if (= (vla-get-LayerOn obj) :vlax-false) (bp:fail "Layer is off. Turn it on first."))
  (setvar "CLAYER" name) (princ (strcat "\nCurrent layer = " name)))
(defun bp:laystate (freeze / name obj)
  (setq name (bp:laypick))
  (if (= (strcase name) (strcase (getvar "CLAYER"))) (bp:fail "Cannot hide/freeze current layer. Choose a different current layer first."))
  (if (member (strcase name) '("0" "DEFPOINTS")) (bp:fail "Layer 0/Defpoints protected by this tool."))
  (setq obj (vla-Item (vla-get-Layers (bp:doc)) name))
  (princ (strcat "\nTarget layer: " name))
  (if (not (bp:yes "\nAffects ALL objects on that layer. Continue? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (if freeze (vla-put-Freeze obj :vlax-true) (vla-put-LayerOn obj :vlax-false))
  (princ "\nLayer changed. U undoes this action; Layer Properties can restore visibility."))
(defun bp:laymatch (/ name layer ss i obj skipped count)
  (setq name (bp:laypick) layer (vla-Item (vla-get-Layers (bp:doc)) name))
  (if (or (= (vla-get-Lock layer) :vlax-true) (= (vla-get-Freeze layer) :vlax-true))
    (bp:fail "Target layer is locked/frozen."))
  (princ (strcat "\nSelect objects to move to layer " name ": "))
  (setq ss (ssget) i 0 skipped 0 count 0)
  (if (not ss) (bp:fail "No selection."))
  (if (not (bp:yes "\nChange selected objects' layers? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i))
    (if (= (vla-get-Lock (vla-Item (vla-get-Layers (bp:doc)) (vla-get-Layer obj))) :vlax-true)
      (setq skipped (1+ skipped))
      (progn (vla-put-Layer obj name) (setq count (1+ count)))))
  (princ (strcat "\nMoved " (itoa count) "; locked-layer objects skipped " (itoa skipped) ". U undoes.")))
(defun bp:theight (/ ss h i obj)
  (setq ss (ssget '((0 . "TEXT,MTEXT"))))
  (if (not ss) (bp:fail "No TEXT/MTEXT selected."))
  (bp:editss ss)
  (initget 7) (setq h (getreal "\nNew text height in drawing units: "))
  (if (not (bp:yes "\nChange selected text heights? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (setq i 0)
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i)) (vla-put-Height obj h))
  (princ "\nHeights set. Annotative/fixed-height styles may control the final appearance; inspect it."))
(defun bp:replaceall (s old new / pos start out)
  (setq start 0 out "")
  (while (setq pos (vl-string-search old s start))
    (setq out (strcat out (if (> pos start) (substr s (1+ start) (- pos start)) "") new) start (+ pos (strlen old))))
  (strcat out (substr s (1+ start))))
(defun bp:treplace (/ ss old new i obj text count)
  (princ "\nPlain single-line TEXT only; not MTEXT, fields or attributes.")
  (setq ss (ssget '((0 . "TEXT")))) (if (not ss) (bp:fail "No TEXT selected."))
  (bp:editss ss)
  (setq old (getstring T "\nFind exact text (case-sensitive): "))
  (if (= old "") (bp:fail "Find text cannot be blank."))
  (setq new (getstring T "\nReplace with (blank allowed): "))
  (if (not (bp:yes "\nReplace all occurrences in selected plain TEXT? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (setq i 0 count 0)
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i) text (vla-get-TextString obj))
    (if (not (vl-string-search "%<" text))
      (if (vl-string-search old text)
        (progn (vla-put-TextString obj (bp:replaceall text old new)) (setq count (1+ count))))))
  (princ (strcat "\nChanged " (itoa count) " TEXT entities. Field expressions were not edited.")))
(defun bp:tinc (/ ss delta i obj s count skipped)
  (setq ss (ssget '((0 . "TEXT")))) (if (not ss) (bp:fail "No TEXT selected."))
  (bp:editss ss)
  (setq delta (getreal "\nAdd this amount to each plain numeric TEXT: "))
  (if (not delta) (bp:fail "Cancelled."))
  (if (not (bp:yes "\nModify numeric text values? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (setq i 0 count 0 skipped 0)
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i) s (bp:trim (vla-get-TextString obj)))
    (if (bp:digits s) (progn (vla-put-TextString obj (bp:num (+ (atof s) delta))) (setq count (1+ count)))
      (setq skipped (1+ skipped))))
  (princ (strcat "\nChanged " (itoa count) "; skipped " (itoa skipped) ". Precision uses BPSET; original formatting is not retained.")))
(defun bp:plclose (/ ss i obj e flags count)
  (setq ss (ssget '((0 . "LWPOLYLINE,POLYLINE"))) i 0 count 0)
  (if (not ss) (bp:fail "No polylines selected."))
  (repeat (sslength ss)
    (setq e (ssname ss i) i (1+ i) flags (cdr (assoc 70 (entget e))))
    (bp:editable e)
    (if (/= 0 (logand flags 86)) (bp:fail "Only ordinary unfitted polylines, not meshes/polyfaces.")))
  (princ "\nClosing adds a segment from each last vertex to its first. Inspect geometry afterwards.")
  (if (not (bp:yes "\nClose selected polylines? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (setq i 0)
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i))
    (if (= (vla-get-Closed obj) :vlax-false) (progn (vla-put-Closed obj :vlax-true) (setq count (1+ count)))))
  (princ (strcat "\nClosed " (itoa count) " polylines. U undoes.")))
(defun bp:attout (/ ss i obj attrs at rows path blockname row)
  (setq ss (ssget '((0 . "INSERT"))) i 0 rows nil)
  (if (not ss) (bp:fail "No block references selected."))
  (repeat (sslength ss)
    (setq obj (vlax-ename->vla-object (ssname ss i)) i (1+ i)
      blockname (if (vlax-property-available-p obj 'EffectiveName) (vla-get-EffectiveName obj) (vla-get-Name obj)))
    (if (= (vla-get-HasAttributes obj) :vlax-true)
      (progn
        (setq attrs (vlax-invoke obj 'GetAttributes))
        (foreach at attrs
          (setq rows (cons (list (vla-get-Handle obj) blockname (vla-get-TagString at) (vla-get-TextString at)) rows))))))
  (if (not rows) (bp:fail "No editable attributes. Constant/nested attributes are not exported."))
  (setq path (getfiled "Save NEW attribute CSV" "BPlus-attributes.csv" "csv" 1))
  (if (not path) (bp:fail "Cancelled."))
  (if (findfile path) (bp:fail "File exists. Choose a new filename."))
  (setq *bp:io* (open path "w")) (if (not *bp:io*) (bp:fail "Cannot write CSV."))
  (write-line "Handle,Block,Tag,Value" *bp:io*)
  (foreach row (reverse rows)
    (write-line (strcat (bp:quote (nth 0 row)) "," (bp:quote (nth 1 row)) "," (bp:quote (nth 2 row)) "," (bp:quote (nth 3 row))) *bp:io*))
  (close *bp:io*) (setq *bp:io* nil)
  (princ (strcat "\nSaved " (itoa (length rows)) " editable attributes to " path)))
(defun c:BPBEAR () (bp:run 'bp:bear))
(defun c:BPLEVEL () (bp:run 'bp:level))
(defun c:BPCHAIN () (bp:run 'bp:chain))
(defun c:BPSETZ () (bp:run '(lambda () (bp:setz nil))))
(defun c:BPSHIFTZ () (bp:run '(lambda () (bp:setz T))))
(defun c:BPCOTAB () (bp:run 'bp:cotab))
(defun c:BPPLTAB () (bp:run 'bp:pltab))
(defun c:BPHATCH () (bp:run 'bp:hatch))
(defun c:BPLAY () (bp:run 'bp:laycurrent))
(defun c:BPLAYOFF () (bp:run '(lambda () (bp:laystate nil))))
(defun c:BPLAYFREEZE () (bp:run '(lambda () (bp:laystate T))))
(defun c:BPLAYMATCH () (bp:run 'bp:laymatch))
(defun c:BPTHEIGHT () (bp:run 'bp:theight))
(defun c:BPTREPLACE () (bp:run 'bp:treplace))
(defun c:BPTINC () (bp:run 'bp:tinc))
(defun c:BPCLOSE () (bp:run 'bp:plclose))
(defun c:BPATTOUT () (bp:run 'bp:attout))
;;; Clickable interface. Documented Windows ActiveX pulldown + DCL fallback.
;;; No automatic saving of the user's CUI or workspace.
(setq *bp:ui-groups*
 '(("Measurements and labels"
    ("Polyline total length" "BPLEN") ("Length leader" "BPLL") ("Area leader" "BPAL")
    ("Coordinate leaders" "BPCO") ("Azimuth + distance leader" "BPBEAR")
    ("Slope / distances" "BPSLOPE") ("Spot elevation leaders" "BPLEVEL") ("Chainage labels" "BPCHAIN"))
   ("Points and file formats"
    ("Import survey / KML points" "BPIN") ("Import Excel points" "BPXL")
    ("Export AutoCAD POINTs" "BPOUT") ("Convert point file" "BPCONV")
    ("Export polyline vertices" "BPVERT") ("Drawing to KML / KMZ" "BPGEO")
    ("Set POINT elevations" "BPSETZ") ("Shift POINT elevations" "BPSHIFTZ"))
   ("Tables and quantities"
    ("Coordinate table" "BPCOTAB") ("Polyline quantity table" "BPPLTAB")
    ("Hatch area total / table" "BPHATCH") ("Count selected blocks" "BPCOUNT")
    ("Export editable block attributes" "BPATTOUT"))
   ("Text and numbering"
    ("Click sequential labels" "BPNUM") ("Sum plain numeric text" "BPTSUM")
    ("Set text height" "BPTHEIGHT") ("Find / replace plain TEXT" "BPTREPLACE")
    ("Add amount to numeric TEXT" "BPTINC"))
   ("Layers and polylines"
    ("Current layer from picked object" "BPLAY") ("Turn picked layer off" "BPLAYOFF")
    ("Freeze picked layer" "BPLAYFREEZE") ("Match objects to picked layer" "BPLAYMATCH")
    ("Close selected polylines" "BPCLOSE"))
   ("Settings and help"
    ("Text height / decimals" "BPSET") ("Parser self-check" "BPTEST")
    ("Command-line menu" "BP") ("Full command help" "BPHELP")
    ("Show / repair top menu" "BPMENU") ("Hide top menu" "BPMENUOFF"))))
(defun bp:uimacro (cmd) (strcat (chr 3) (chr 3) cmd " "))
(defun bp:ownedmenu (menu / first)
  (and (> (vla-get-Count menu) 0)
    (setq first (vla-Item menu 0))
    (= (vla-get-TagString first) "BPLUS_CAD_TOOLS_MENU_MARKER")))
(defun bp:findmenu (/ group menu found)
  (vlax-for group (vla-get-MenuGroups (vlax-get-acad-object))
    (vlax-for menu (vla-get-Menus group)
      (if (bp:ownedmenu menu) (setq found menu)))) found)
(defun bp:buildmenu (/ app group menus menu item section sub pair result)
  (setq app (vlax-get-acad-object) menu (bp:findmenu))
  (if menu
    (progn
      (if (= (vla-get-OnMenuBar menu) :vlax-true) (vla-RemoveFromMenuBar menu))
      (while (> (vla-get-Count menu) 1) (vla-Delete (vla-Item menu 1)))
      ;; Preserve our marker until the rebuild finishes, so a partial rebuild
      ;; remains recognizable and can be repaired without duplicate menus.
      (vla-put-Label (vla-Item menu 0) "BPlus CAD Tools 1.1 - TRIAL / Help"))
    (progn
      (setq group (vla-Item (vla-get-MenuGroups app) 0) menus (vla-get-Menus group))
      (setq result (vl-catch-all-apply 'vla-Item (list menus "BPlus")))
      (if (vl-catch-all-error-p result) (setq menu (vla-Add menus "BPlus"))
        (setq menu (vla-Add menus "BPlus CAD Tools")))
      (setq item (vla-AddMenuItem menu "" "BPlus CAD Tools 1.1 - TRIAL / Help" (bp:uimacro "BPHELP")))
      (vla-put-TagString item "BPLUS_CAD_TOOLS_MENU_MARKER")))
  (vla-AddMenuItem menu "" "Open clickable tools panel..." (bp:uimacro "BPPANEL"))
  (vla-AddSeparator menu "")
  (foreach section *bp:ui-groups*
    (setq sub (vla-AddSubMenu menu "" (car section)))
    (foreach pair (cdr section)
      (vla-AddMenuItem sub "" (car pair) (bp:uimacro (cadr pair)))))
  ;; String-index append is documented and avoids empty-menu index ambiguity.
  (vla-InsertMenuInMenuBar (vla-get-Parent menu) (vla-get-Name menu) "")
  (setvar "MENUBAR" 1)
  (if (/= (vla-get-OnMenuBar menu) :vlax-true) (bp:fail "AutoCAD did not show the BPlus menu."))
  (princ "\nBPlus menu ready at the top. Grouped commands are clickable. CAD/UI runtime testing still required.") T)
(defun bp:showmenu (/ result)
  (setq result (vl-catch-all-apply 'bp:buildmenu nil))
  (if (vl-catch-all-error-p result)
    (princ (strcat "\nBPlus top menu unavailable: " (vl-catch-all-error-message result)
      "\nFallback: BPPANEL opens clickable buttons; BP gives a text menu. All individual commands remain available.")))
  (princ))
(defun c:BPMENU () (bp:showmenu))
(defun c:BPMENUOFF (/ menu)
  (setq menu (bp:findmenu))
  (if (and menu (= (vla-get-OnMenuBar menu) :vlax-true)) (vla-RemoveFromMenuBar menu))
  (princ "\nBPlus menu hidden for this session. Other menus/settings were not hidden. BPMENU restores it.") (princ))
(defun bp:panelwrite (path / section pair idx row)
  (setq *bp:io* (open path "w"))
  (if (not *bp:io*) (bp:fail "Cannot create local dialog definition."))
  (write-line "bplus : dialog { label = \"BPlus CAD Tools 1.1 - TRIAL\";" *bp:io*)
  (write-line ": text { label = \"Static CAD/UI checks only. Use a drawing copy.\"; }" *bp:io*)
  (setq idx 0)
  (foreach section *bp:ui-groups*
    (write-line (strcat ": boxed_column { label = \"" (car section) "\";") *bp:io*)
    (setq row 0)
    (foreach pair (cdr section)
      (if (= 0 (rem row 3)) (write-line ": row {" *bp:io*))
      (write-line (strcat ": button { key = \"b" (itoa idx) "\"; label = \"" (car pair) "\"; width = 35; fixed_width = true; }") *bp:io*)
      (setq idx (1+ idx) row (1+ row))
      (if (= 0 (rem row 3)) (write-line "}" *bp:io*)))
    (if (/= 0 (rem row 3)) (write-line "}" *bp:io*))
    (write-line "}" *bp:io*))
  (write-line "spacer; cancel_button; }" *bp:io*)
  (close *bp:io*) (setq *bp:io* nil))
(defun c:BPPANEL (/ *error* path dialogid choice idx section pair result)
  (setq path (vl-filename-mktemp "BPUI" nil ".dcl") dialogid nil choice nil)
  (defun *error* (msg)
    (if dialogid (unload_dialog dialogid))
    (if *bp:io* (close *bp:io*)) (setq *bp:io* nil)
    (if (and path (findfile path)) (vl-file-delete path))
    (if msg (princ (strcat "\nBPlus panel: " msg)))
    (princ "\nFallback: BP or BPHELP.") (princ))
  (bp:panelwrite path)
  (setq dialogid (load_dialog path))
  (if (or (< dialogid 0) (not (new_dialog "bplus" dialogid))) (bp:fail "Could not load buttons dialog."))
  (setq idx 0)
  (foreach section *bp:ui-groups*
    (foreach pair (cdr section)
      (action_tile (strcat "b" (itoa idx)) (strcat "(setq choice \"" (cadr pair) "\")(done_dialog 1)"))
      (setq idx (1+ idx))))
  (action_tile "cancel" "(done_dialog 0)")
  (start_dialog) (unload_dialog dialogid) (setq dialogid nil)
  (vl-file-delete path) (setq path nil)
  ;; choice comes only from our fixed internal button list, never file input.
  (if choice (apply (read (strcat "c:" choice)) nil))
  (princ))
(defun bp:excelread (/ path fmt sheets bridge line index mapping cols col n header)
  (setq path (getfiled "Excel point worksheet" "" "xlsx;xls" 0))
  (if (not path) (bp:fail "Cancelled."))
  (setq fmt (strcase (vl-filename-extension path)))
  (if (/= fmt ".XLSX") (bp:fail "Use .xlsx. For legacy .xls, Save As .xlsx first. Excel itself is never opened by this reader."))
  (setq fmt (substr fmt 2) sheets (bp:temp ".sheets.txt"))
  (bp:helper "SheetList" path sheets fmt nil)
  (setq *bp:io* (open sheets "r"))
  (if (not *bp:io*) (bp:fail "Cannot read worksheet list."))
  (princ "\nWorksheets (workbook order, non-ASCII name characters appear as ?):")
  (while (setq line (read-line *bp:io*)) (princ (strcat "\n" line)))
  (close *bp:io*) (setq *bp:io* nil)
  (initget 7) (setq index (getint "\nWorksheet number from the list: "))
  (setq mapping "")
  (foreach col '("P point ID (or - for automatic IDs)" "E Easting X" "N Northing Y" "Z elevation" "D description (or - for none)")
    (setq line (strcase (bp:trim (getstring (strcat "\nExcel column letters for " col ": ")))))
    (if (= line "") (bp:fail "Column mapping cannot be blank. Use - only for optional P/D."))
    (setq mapping (if (= mapping "") line (strcat mapping "," line))))
  (setq header (bp:yes "\nSkip first nonblank mapped row as header? [Yes/No] <No>: "))
  (princ (strcat "\nSheet=" (itoa index) "; columns in P,E,N,Z,D order=" mapping))
  (princ "\nFormula cells are rejected. IDs use underlying cell values, not number-format display. No CRS/unit conversion.")
  (if (not (bp:yes "\nRead this sheet with this mapping? [Yes/No] <No>: ")) (bp:fail "Cancelled."))
  (setq bridge (bp:temp ".csv") *bp:excelopts* (list index mapping (if header "1" "0")))
  (bp:helper "Read" path bridge fmt nil)
  (setq *bp:excelopts* nil)
  (bp:checkrows (bp:readrows bridge "PENZD" "," nil)))
(defun bp:xl (/ rows)
  (bp:model) (setq rows (bp:excelread)) (bp:importrows rows))
(defun c:BPXL () (bp:run 'bp:xl))

;;; Embedded local conversion helper.
(setq *bp:ps-lines*
  '(
    "param("
    " [ValidateSet('Read','Write','GeoWrite','SheetList','SelfTest')][string]$Mode,"
    " [string]$Source,[string]$Target,[string]$Format,"
    " [ValidateSet('None','LonLat','UTM')][string]$Crs='None',"
    " [int]$Zone=0,[ValidateSet('N','S')][string]$Hemi='N',"
    " [ValidateSet('Zero','Absolute','Clamp')][string]$Altitude='Zero',"
    " [int]$Sheet=1,[string]$Columns='A,B,C,D,E',[ValidateSet(0,1)][int]$SkipHeader=1,"
    " [string]$Status"
    ")"
    "# Local file processing only. No downloads, external services, or evaluated input."
    "$ErrorActionPreference='Stop'"
    "$CI=[Globalization.CultureInfo]::InvariantCulture"
    "[Threading.Thread]::CurrentThread.CurrentCulture=$CI"
    "[Threading.Thread]::CurrentThread.CurrentUICulture=$CI"
    "$Ascii=[Text.Encoding]::ASCII"
    "$Utf8=New-Object Text.UTF8Encoding($false)"
    "function Need($ok,[string]$message) { if(-not $ok){throw $message} }"
    "function Num([string]$s) {"
    " Need ($s.Trim() -match '^[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)$') 'Invalid decimal coordinate (dot decimal required).'"
    " $v=[double]::Parse($s.Trim(),$CI)"
    " Need (-not [double]::IsInfinity($v) -and [math]::Abs($v) -le 1e12) 'Coordinate out of supported range.'"
    " return $v"
    "}"
    "function AsciiText([string]$s,[int]$max) {"
    " Need ($s.Length -le $max -and $s -notmatch '[^\\x20-\\x7E]') 'IDs/descriptions must be printable ASCII and within field width.'"
    "}"
    "function CheckRows($rows) {"
    " Need ($rows.Count -gt 0 -and $rows.Count -le 20000) 'Need 1..20000 points.'"
    " $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)"
    " foreach($r in $rows){"
    "  Need (-not [string]::IsNullOrWhiteSpace($r.P)) 'Blank point ID.'"
    "  AsciiText $r.P 200; AsciiText $r.D 200"
    "  Need ($seen.Add([string]$r.P)) ('Duplicate point ID: '+$r.P)"
    "  foreach($key in @('E','N','Z')){"
    "   $v=[double]$r.$key; Need (-not [double]::IsNaN($v) -and -not [double]::IsInfinity($v) -and [math]::Abs($v) -le 1e12) 'Invalid coordinate.'"
    "  }"
    " }"
    "}"
    "function Row($p,$e,$n,$z,$d) {"
    " [pscustomobject]@{P=[string]$p;E=[double]$e;N=[double]$n;Z=[double]$z;D=[string]$d;G='';K='Point'}"
    "}"
    "function ReadBridge([string]$path) {"
    " $out=@(Import-Csv -LiteralPath $path -Header P,E,N,Z,D,G,K | ForEach-Object {"
    "  $r=Row $_.P (Num $_.E) (Num $_.N) (Num $_.Z) $_.D"
    "  if($_.G){$r.G=$_.G};if($_.K){$r.K=$_.K};$r"
    " })"
    " CheckRows $out"
    " return $out"
    "}"
    "function Q([string]$s) { return '\"'+$s.Replace('\"','\"\"')+'\"' }"
    "function WriteBridge($rows,[string]$path) {"
    " $ls=@(foreach($r in $rows){"
    "  (Q $r.P)+','+$r.E.ToString('F8',$CI)+','+$r.N.ToString('F8',$CI)+','+$r.Z.ToString('F8',$CI)+','+(Q $r.D)"
    " })"
    " [IO.File]::WriteAllLines($path,[string[]]$ls,$Ascii)"
    "}"
    "function ReadGsi([string]$path) {"
    " $rows=New-Object 'System.Collections.Generic.List[object]'"
    " $lineNo=0"
    " foreach($ln in [IO.File]::ReadAllLines($path)){"
    "  $lineNo++;$s=$ln.Trim();if(-not $s){continue}"
    "  $size=8;if($s.StartsWith('*')){$size=16;$s=$s.Substring(1)}"
    "  $words=$s -split '\\s+';$parts=@{}"
    "  foreach($w in $words){"
    "   Need ($w.Length -eq (7+$size) -and $w -match '^[0-9]{2}[0-9.]{4}[+-][!-~]+$') \"Malformed GSI word at line $lineNo.\""
    "   $wi=$w.Substring(0,2)"
    "   Need (@('11','81','82','83','71') -contains $wi) \"GSI line ${lineNo}: only coordinate records 11/81/82/83 and optional 71 supported. Export coordinates, not raw observations/stations.\""
    "   Need (-not $parts.ContainsKey($wi)) \"Repeated GSI word at line $lineNo.\""
    "   $data=$w.Substring(7)"
    "   if($wi -in @('81','82','83')){"
    "    Need ($data -match '^\\d+$') \"Non-numeric GSI coordinate at line $lineNo.\""
    "    $unit=$w.Substring(5,1)"
    "    $scale=switch($unit){'0'{1000.0};'6'{10000.0};'8'{100000.0};default{throw \"GSI line ${lineNo}: only metric unit codes 0,6,8 supported (feet rejected).\"}}"
    "    $parts[$wi]=[double]::Parse($w.Substring(6,1)+$data,$CI)/$scale"
    "   }else{"
    "    Need ($w.Substring(6,1) -eq '+') 'Signed GSI text not supported.'"
    "    $v=$data.TrimStart([char]'0');if(-not $v){$v='0'};$parts[$wi]=$v"
    "   }"
    "  }"
    "  foreach($wi in @('11','81','82','83')){Need ($parts.ContainsKey($wi)) \"Incomplete GSI coordinate at line $lineNo (need 11/81/82/83).\"}"
    "  $d='';if($parts.ContainsKey('71')){$d=$parts['71']}"
    "  $rows.Add((Row $parts['11'] $parts['81'] $parts['82'] $parts['83'] $d))"
    " }"
    " CheckRows $rows"
    " return $rows.ToArray()"
    "}"
    "function GsiCoord([double]$v,[int]$size,[string]$wi){"
    " $n=[decimal][math]::Round([math]::Abs($v)*1000.0,0,[MidpointRounding]::AwayFromZero)"
    " $digits=$n.ToString('0',$CI);Need ($digits.Length -le $size) 'GSI coordinate exceeds field width. Choose GSI16.'"
    " $sgn='+';if($v -lt 0){$sgn='-'}"
    " return $wi+'..00'+$sgn+$digits.PadLeft($size,'0')"
    "}"
    "function GsiText([string]$s,[int]$size,[string]$wi){"
    " AsciiText $s $size"
    " Need ($s -match '^[A-Za-z0-9_.-]+$') 'GSI IDs/codes allow only letters, digits, underscore, dot and hyphen.'"
    " Need ($s -eq '0' -or -not $s.StartsWith('0')) 'GSI text with leading zero is ambiguous padding. Rename ID/code first.'"
    " return $wi+'....+'+$s.PadLeft($size,'0')"
    "}"
    "function WriteGsi($rows,[string]$path,[int]$size){"
    " $ls=@(foreach($r in $rows){"
    "  $s=(GsiText $r.P $size '11')+' '+(GsiCoord $r.E $size '81')+' '+(GsiCoord $r.N $size '82')+' '+(GsiCoord $r.Z $size '83')"
    "  if($r.D){$s+=' '+(GsiText $r.D $size '71')}"
    "  if($size -eq 16){$s='*'+$s};$s+' '"
    " })"
    " [IO.File]::WriteAllLines($path,[string[]]$ls,$Ascii)"
    "}"
    "function ReadSdr([string]$path){"
    " $rows=New-Object 'System.Collections.Generic.List[object]';$header=$false;$ln=0;$ignored=0"
    " foreach($s in [IO.File]::ReadAllLines($path)){"
    "  $ln++;if(-not $s.Trim()){continue}"
    "  Need ($s.Length -ge 4 -and $s.Substring(0,4) -match '^\\d{2}[A-Z0-9]{2}$') \"Malformed SDR record at line $ln.\""
    "  $type=$s.Substring(0,2)"
    "  if($type -eq '00'){"
    "   Need (-not $header -and $s.Length -ge 46 -and $s.Substring(4,16).Contains('SDR33')) 'Only SDR33 with a valid 46-character header supported (not SDR20/22).'"
    "   Need ($s.Substring(41,1) -eq '1' -and $s.Substring(44,1) -eq '1') 'SDR33 must specify meters and N-E-H coordinate prompt order.'"
    "   $header=$true;continue"
    "  }"
    "  Need $header 'SDR33 header must precede data.'"
    "  if($type -eq '08'){"
    "   Need ($s.Length -ge 68 -and ($s.Length -le 84 -or -not $s.Substring(84).Trim())) \"Bad SDR33 coordinate record length at line $ln.\""
    "   $d='';if($s.Length -gt 68){$d=$s.Substring(68,[math]::Min(16,$s.Length-68)).Trim()}"
    "   $rows.Add((Row $s.Substring(4,16).Trim() (Num $s.Substring(36,16)) (Num $s.Substring(20,16)) (Num $s.Substring(52,16)) $d))"
    "  }elseif($type -in @('01','02','03','04','05','06','07','09','10','11','13')){$ignored++}"
    "  else{throw \"Unsupported SDR record $type at line $ln.\"}"
    " }"
    " CheckRows $rows"
    " $script:Notice=\"SDR33: $ignored non-08 records ignored; raw observations are NOT reduced.\""
    " return $rows.ToArray()"
    "}"
    "function SdrField([string]$s){AsciiText $s 16;return $s.PadRight(16,' ')}"
    "function WriteSdr($rows,[string]$path){"
    " $header='00NM'+('SDR33 V04-04.02'.PadRight(16,' '))+'0000'+((Get-Date).ToString('yy-MM-dd HH:mm').PadRight(16,' '))+'111111'"
    " $ls=@($header,('10NM'+('BPLUS'.PadRight(16,' '))+'121111'))"
    " $ls+=@(foreach($r in $rows){"
    "  '08TP'+(SdrField $r.P)+(SdrField $r.N.ToString('F4',$CI))+(SdrField $r.E.ToString('F4',$CI))+(SdrField $r.Z.ToString('F4',$CI))+(SdrField $r.D)"
    " })"
    " [IO.File]::WriteAllLines($path,[string[]]$ls,$Ascii)"
    "}"
    "# WGS84 transverse Mercator, Kruger n^3 series. Restricted UTM envelope."
    "$script:nn=(1.0/298.257223563)/(2.0-1.0/298.257223563)"
    "$script:aa=6378137.0/(1+$nn)*(1+$nn*$nn/4+[math]::Pow($nn,4)/64)"
    "$script:alpha=@(0,($nn/2-2*$nn*$nn/3+5*[math]::Pow($nn,3)/16),(13*$nn*$nn/48-3*[math]::Pow($nn,3)/5),(61*[math]::Pow($nn,3)/240))"
    "$script:beta=@(0,($nn/2-2*$nn*$nn/3+37*[math]::Pow($nn,3)/96),($nn*$nn/48+[math]::Pow($nn,3)/15),(17*[math]::Pow($nn,3)/480))"
    "$script:delta=@(0,(2*$nn-2*$nn*$nn/3-2*[math]::Pow($nn,3)),(7*$nn*$nn/3-8*[math]::Pow($nn,3)/5),(56*[math]::Pow($nn,3)/15))"
    "function Atanh([double]$x){0.5*[math]::Log((1+$x)/(1-$x))}"
    "function CheckLL([double]$lon,[double]$lat){Need ([math]::Abs($lon) -le 180 -and [math]::Abs($lat) -le 90) 'Longitude/latitude outside valid range.'}"
    "function CheckZone{Need ($Zone -ge 1 -and $Zone -le 60) 'UTM zone must be 1..60.'}"
    "function ToUTM([double]$lon,[double]$lat){"
    " CheckLL $lon $lat;CheckZone"
    " Need ($lat -ge -80 -and $lat -le 84) 'UTM supports latitude -80..84 degrees.'"
    " Need (($Hemi -eq 'N' -and $lat -ge -1e-10) -or ($Hemi -eq 'S' -and $lat -le 1e-10)) 'Latitude conflicts with chosen hemisphere.'"
    " $dl=$lon-($Zone*6-183);if($dl -gt 180){$dl-=360};if($dl -lt -180){$dl+=360}"
    " Need ([math]::Abs($dl) -le 6.0) 'Point too far from chosen UTM zone (limit 6 degrees from central meridian).'"
    " $p=$lat*[math]::PI/180;$l=$dl*[math]::PI/180"
    " $ecc=2*[math]::Sqrt($nn)/(1+$nn)"
    " $t=[math]::Sinh((Atanh ([math]::Sin($p)))-$ecc*(Atanh ($ecc*[math]::Sin($p))))"
    " $xi=[math]::Atan2($t,[math]::Cos($l));$eta=Atanh ([math]::Sin($l)/[math]::Sqrt(1+$t*$t))"
    " $x=$eta;$y=$xi"
    " for($j=1;$j -le 3;$j++){"
    "  $x+=$alpha[$j]*[math]::Cos(2*$j*$xi)*[math]::Sinh(2*$j*$eta)"
    "  $y+=$alpha[$j]*[math]::Sin(2*$j*$xi)*[math]::Cosh(2*$j*$eta)"
    " }"
    " $east=500000+0.9996*$aa*$x;$north=0.9996*$aa*$y;if($Hemi -eq 'S'){$north+=10000000}"
    " Need ($east -ge 100000 -and $east -le 900000 -and $north -ge 0 -and $north -le 10000000) 'Point outside supported UTM coordinate envelope.'"
    " return @($east,$north)"
    "}"
    "function FromUTM([double]$east,[double]$north){"
    " CheckZone"
    " Need ($east -ge 100000 -and $east -le 900000 -and $north -ge 0 -and $north -le 10000000) 'UTM expects meters: E=100000..900000, N=0..10000000.'"
    " if($Hemi -eq 'S'){$north-=10000000}"
    " $xi=$north/(0.9996*$aa);$eta=($east-500000)/(0.9996*$aa);$xp=$xi;$ep=$eta"
    " for($j=1;$j -le 3;$j++){"
    "  $xp-=$beta[$j]*[math]::Sin(2*$j*$xi)*[math]::Cosh(2*$j*$eta)"
    "  $ep-=$beta[$j]*[math]::Cos(2*$j*$xi)*[math]::Sinh(2*$j*$eta)"
    " }"
    " $chi=[math]::Asin([math]::Sin($xp)/[math]::Cosh($ep));$lat=$chi"
    " for($j=1;$j -le 3;$j++){$lat+=$delta[$j]*[math]::Sin(2*$j*$chi)}"
    " $lat*=180/[math]::PI;$lon=($Zone*6-183)+[math]::Atan2([math]::Sinh($ep),[math]::Cos($xp))*180/[math]::PI"
    " if($lon -gt 180){$lon-=360};if($lon -lt -180){$lon+=360}"
    " $check=ToUTM $lon $lat"
    " Need ([math]::Abs($check[0]-$east) -lt 0.02 -and [math]::Abs($check[1]-($north+$(if($Hemi -eq 'S'){10000000}else{0}))) -lt 0.02) 'UTM round-trip check failed.'"
    " return @($lon,$lat)"
    "}"
    "function LoadKml([string]$path){"
    " Need ((Get-Item -LiteralPath $path).Length -le 33554432) 'KML/KMZ exceeds 32 MB input limit.'"
    " $stream=$null;$zip=$null"
    " try{"
    "  if([IO.Path]::GetExtension($path).ToLowerInvariant() -eq '.kmz'){"
    "   Add-Type -AssemblyName System.IO.Compression.FileSystem"
    "   $zip=[IO.Compression.ZipFile]::OpenRead($path)"
    "   $ks=@($zip.Entries | Where-Object {$_.FullName -match '(?i)\\.kml$'})"
    "   $main=@($ks|Where-Object {$_.FullName -ieq 'doc.kml'})"
    "   if($main.Count -eq 1){$entry=$main[0]}else{Need ($ks.Count -eq 1) 'KMZ needs doc.kml or exactly one KML entry.';$entry=$ks[0]}"
    "   Need ($entry.Length -le 33554432) 'Uncompressed KML exceeds 32 MB limit.';$stream=$entry.Open()"
    "  }else{$stream=[IO.File]::OpenRead($path)}"
    "  $settings=New-Object Xml.XmlReaderSettings"
    "  $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=33554432"
    "  $reader=[Xml.XmlReader]::Create($stream,$settings)"
    "  try{$xml=New-Object Xml.XmlDocument;$xml.XmlResolver=$null;$xml.Load($reader)}finally{$reader.Dispose()}"
    "  return ,$xml"
    " }finally{if($stream){$stream.Dispose()};if($zip){$zip.Dispose()}}"
    "}"
    "function ReadKml([string]$path){"
    " Need ($Crs -ne 'None') 'Explicit target CRS required.'"
    " $xml=LoadKml $path"
    " Need ($xml.DocumentElement.LocalName -eq 'kml' -and $xml.DocumentElement.NamespaceURI -eq 'http://www.opengis.net/kml/2.2') 'Only KML 2.2 supported.'"
    " Need ($xml.SelectNodes('//*[local-name()=\"NetworkLink\"]').Count -eq 0) 'NetworkLink KML is not supported. No linked content is downloaded.'"
    " Need ($xml.SelectNodes('//*[local-name()=\"LineString\" or local-name()=\"Polygon\" or local-name()=\"Model\" or local-name()=\"Track\"]').Count -eq 0) 'Point-only import: this KML contains lines/polygons/models/tracks.'"
    " $rows=New-Object 'System.Collections.Generic.List[object]';$serial=0"
    " foreach($pm in $xml.SelectNodes('//*[local-name()=\"Placemark\"]')){"
    "  $pts=$pm.SelectNodes('.//*[local-name()=\"Point\"]')"
    "  Need ($pts.Count -eq 1) 'Each Placemark must contain exactly one Point.'"
    "  $pt=$pts[0];$co=$pt.SelectNodes('./*[local-name()=\"coordinates\"]');Need ($co.Count -eq 1) 'Missing/duplicate Point coordinates.'"
    "  $cs=$co[0].InnerText.Trim() -split ','"
    "  Need ($cs.Count -in @(2,3)) 'Point coordinates must be one lon,lat[,alt] tuple.'"
    "  $lon=Num $cs[0];$lat=Num $cs[1];CheckLL $lon $lat;$z=0.0"
    "  if($Altitude -eq 'Absolute'){"
    "   $am=$pt.SelectSingleNode('./*[local-name()=\"altitudeMode\"]')"
    "   Need ($null -ne $am -and $am.InnerText -eq 'absolute' -and $cs.Count -eq 3) 'Absolute import requires altitudeMode=absolute and explicit altitude on every point. Otherwise choose Zero.'"
    "   $z=Num $cs[2]"
    "  }"
    "  $en=@($lon,$lat);if($Crs -eq 'UTM'){$en=ToUTM $lon $lat}"
    "  $serial++;$name=$pm.SelectSingleNode('./*[local-name()=\"name\"]');$d=$pm.SelectSingleNode('./*[local-name()=\"description\"]')"
    "  $id=[string]$serial;if($null -ne $name -and $name.InnerText.Trim()){$id=$name.InnerText.Trim()}"
    "  $desc='';if($null -ne $d){$desc=$d.InnerText.Trim()}"
    "  $rows.Add((Row $id $en[0] $en[1] $z $desc))"
    " }"
    " CheckRows $rows"
    " return $rows.ToArray()"
    "}"
    "function WriteKml($rows,[string]$path){"
    " Need ($Crs -ne 'None') 'Explicit source CRS required.'"
    " $mem=New-Object IO.MemoryStream"
    " $set=New-Object Xml.XmlWriterSettings;$set.Encoding=$Utf8;$set.Indent=$true"
    " $w=[Xml.XmlWriter]::Create($mem,$set)"
    " $ns='http://www.opengis.net/kml/2.2'"
    " function Element([string]$name,[string]$value){$w.WriteElementString($name,$ns,$value)}"
    " try{"
    "  $w.WriteStartDocument();$w.WriteStartElement('kml',$ns);$w.WriteStartElement('Document',$ns)"
    "  Element 'name' 'BPlus export'"
    "  Element 'description' (\"WGS84 horizontal; source=$Crs zone=$Zone hemisphere=$Hemi; no vertical datum/geoid transformation; altitude=$Altitude; check control points.\")"
    "  $groups=@($rows | Group-Object -Property {if($_.K -eq 'Line'){'L:'+ $_.G}else{'P:'+ $_.P}})"
    "  foreach($g in $groups){"
    "   $rr=@($g.Group);$isLine=$rr[0].K -eq 'Line'"
    "   Need (-not $isLine -or $rr.Count -ge 2) 'LineString requires at least two coordinates.'"
    "   $w.WriteStartElement('Placemark',$ns);$name=$rr[0].P;if($isLine){$name=$rr[0].G}"
    "   Element 'name' $name;Element 'description' $rr[0].D"
    "   $tag='Point';if($isLine){$tag='LineString'};$w.WriteStartElement($tag,$ns)"
    "   $am='clampToGround';if($Altitude -eq 'Absolute'){$am='absolute'}"
    "   Element 'altitudeMode' $am"
    "   $tuples=@(foreach($r in $rr){"
    "    $ll=@($r.E,$r.N);if($Crs -eq 'UTM'){$ll=FromUTM $r.E $r.N};CheckLL $ll[0] $ll[1]"
    "    $z=0.0;if($Altitude -eq 'Absolute'){$z=$r.Z}"
    "    $ll[0].ToString('F10',$CI)+','+$ll[1].ToString('F10',$CI)+','+$z.ToString('F4',$CI)"
    "   })"
    "   Element 'coordinates' ($tuples -join ' ')"
    "   $w.WriteEndElement();$w.WriteEndElement()"
    "  }"
    "  $w.WriteEndElement();$w.WriteEndElement();$w.WriteEndDocument();$w.Flush()"
    "  $bytes=$mem.ToArray()"
    " }finally{$w.Dispose();$mem.Dispose()}"
    " if([IO.Path]::GetExtension($path).ToLowerInvariant() -eq '.kmz'){"
    "  Add-Type -AssemblyName System.IO.Compression.FileSystem"
    "  $zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)"
    "  try{$entry=$zip.CreateEntry('doc.kml');$st=$entry.Open();try{$st.Write($bytes,0,$bytes.Length)}finally{$st.Dispose()}}finally{$zip.Dispose()}"
    " }else{[IO.File]::WriteAllBytes($path,$bytes)}"
    "}"
    "# Excel input: Open XML XLSX only. Excel itself is never launched."
    "function ZipXml($zip,[string]$name){"
    " $entry=$zip.GetEntry($name);Need ($null -ne $entry -and $entry.Length -le 33554432) ('Missing/oversized XLSX part: '+$name)"
    " $stream=$entry.Open()"
    " try{"
    "  $settings=New-Object Xml.XmlReaderSettings;$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit"
    "  $settings.XmlResolver=$null;$settings.MaxCharactersInDocument=33554432"
    "  $reader=[Xml.XmlReader]::Create($stream,$settings)"
    "  try{$xml=New-Object Xml.XmlDocument;$xml.XmlResolver=$null;$xml.Load($reader)}finally{$reader.Dispose()}"
    "  return ,$xml"
    " }finally{$stream.Dispose()}"
    "}"
    "function OpenXlsx([string]$path){"
    " Add-Type -AssemblyName System.IO.Compression.FileSystem"
    " return [IO.Compression.ZipFile]::OpenRead($path)"
    "}"
    "function SheetNodes($zip){"
    " $book=ZipXml $zip 'xl/workbook.xml'"
    " Need ($book.DocumentElement.NamespaceURI -eq 'http://schemas.openxmlformats.org/spreadsheetml/2006/main') 'Only standard Transitional XLSX workbooks supported (not Strict XLSX).'"
    " return $book.SelectNodes('//*[local-name()=\"sheets\"]/*[local-name()=\"sheet\"]')"
    "}"
    "function ExcelSheets([string]$path,[string]$fmt){"
    " Need ($fmt -eq 'XLSX') 'Legacy XLS is not opened by this build. Save a trusted workbook as XLSX and retry.'"
    " $zip=OpenXlsx $path"
    " try{return @(SheetNodes $zip | ForEach-Object {$_.GetAttribute('name')})}finally{$zip.Dispose()}"
    "}"
    "function ColumnNumber([string]$s){"
    " Need ($s -match '^[A-Z]{1,3}$') 'Columns must be letters A..XFD or dash for optional P/D.'"
    " $n=0;foreach($ch in $s.ToCharArray()){$n=$n*26+([int]$ch-64)}"
    " Need ($n -le 16384) 'Excel column exceeds XFD.';return $n"
    "}"
    "function ExcelMapping {"
    " $cols=$Columns.ToUpperInvariant().Split(',');Need ($cols.Count -eq 5) 'Five column entries required in P,E,N,Z,D order.'"
    " $used=@{}"
    " for($i=0;$i -lt 5;$i++){"
    "  if($cols[$i] -eq '-') {Need ($i -eq 0 -or $i -eq 4) 'E,N,Z columns are mandatory.';continue}"
    "  [void](ColumnNumber $cols[$i]);Need (-not $used.ContainsKey($cols[$i])) 'Mapped columns must be distinct.';$used[$cols[$i]]=$true"
    " }"
    " return ,$cols"
    "}"
    "function ExcelRow($vals,[int]$serial){"
    " $id=$vals[0];if($id -eq '__AUTO__'){$id=[string]$serial}"
    " return Row $id (Num $vals[1]) (Num $vals[2]) (Num $vals[3]) $vals[4]"
    "}"
    "function ReadXlsx([string]$path){"
    " $cols=ExcelMapping;$zip=OpenXlsx $path"
    " try{"
    "  $sheets=@(SheetNodes $zip);Need ($Sheet -ge 1 -and $Sheet -le $sheets.Count) 'Worksheet index out of range.'"
    "  $node=$sheets[$Sheet-1];$relid=$node.GetAttribute('id','http://schemas.openxmlformats.org/officeDocument/2006/relationships')"
    "  $rels=ZipXml $zip 'xl/_rels/workbook.xml.rels'"
    "  $rr=@($rels.DocumentElement.ChildNodes|Where-Object {$_.LocalName -eq 'Relationship' -and $_.GetAttribute('Id') -eq $relid})"
    "  Need ($rr.Count -eq 1 -and $rr[0].GetAttribute('TargetMode') -ne 'External') 'Missing/external worksheet relationship.'"
    "  $target=$rr[0].GetAttribute('Target').Replace('\\','/')"
    "  Need ($target -notmatch '\\.\\.' -and $target -notmatch ':') 'Unsupported worksheet path.'"
    "  if($target.StartsWith('/')){$target=$target.TrimStart('/')}else{$target='xl/'+$target}"
    "  Need ($target.StartsWith('xl/worksheets/')) 'Only worksheet cell data is supported, not chart sheets.'"
    "  $xml=ZipXml $zip $target"
    "  $strings=@()"
    "  if($zip.GetEntry('xl/sharedStrings.xml')){"
    "   $sx=ZipXml $zip 'xl/sharedStrings.xml'"
    "   $strings=@(foreach($si in $sx.SelectNodes('//*[local-name()=\"si\"]')){"
    "    (@($si.SelectNodes('./*[local-name()=\"t\"] | ./*[local-name()=\"r\"]/*[local-name()=\"t\"]')|ForEach-Object {$_.InnerText}) -join '')"
    "   })"
    "  }"
    "  $rows=New-Object 'System.Collections.Generic.List[object]';$skip=$SkipHeader -eq 1;$serial=0"
    "  foreach($rn in $xml.SelectNodes('//*[local-name()=\"sheetData\"]/*[local-name()=\"row\"]')){"
    "   $cells=@{};foreach($cn in $rn.SelectNodes('./*[local-name()=\"c\"]')){"
    "    $ref=$cn.GetAttribute('r');Need ($ref -match '^([A-Z]{1,3})[1-9][0-9]*$') 'XLSX cells require explicit A1 references.'"
    "    $col=$Matches[1];if($cols -notcontains $col){continue}"
    "    Need (-not $cells.ContainsKey($col)) 'Duplicate XLSX cell reference.'"
    "    $type=$cn.GetAttribute('t');$v=$cn.SelectSingleNode('./*[local-name()=\"v\"]');$value='';if($v){$value=$v.InnerText}"
    "    if($type -eq 's'){"
    "     Need ($value -match '^\\d+$' -and [int]$value -lt $strings.Count) 'Invalid shared string index.';$value=$strings[[int]$value]"
    "    }elseif($type -eq 'inlineStr'){"
    "     $value=(@($cn.SelectNodes('./*[local-name()=\"is\"]//*[local-name()=\"t\"]')|ForEach-Object {$_.InnerText}) -join '')"
    "    }elseif($type -notin @('','n','str')){throw ('Unsupported XLSX cell type at '+$ref)}"
    "    elseif($type -in @('','n') -and $value -match '^[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)[eE][+-]?\\d+$'){"
    "     $number=[double]::Parse($value,[Globalization.NumberStyles]::Float,$CI)"
    "     Need (-not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)) 'Invalid numeric Excel cell.'"
    "     $value=$number.ToString('0.################',$CI)"
    "    }"
    "    $cells[$col]=@([string]$value,($null -ne $cn.SelectSingleNode('./*[local-name()=\"f\"]')))"
    "   }"
    "   $vals=@(for($i=0;$i -lt 5;$i++){"
    "    if($cols[$i] -eq '-') {if($i -eq 0){'__AUTO__'}else{''}}"
    "    elseif($cells.ContainsKey($cols[$i])){$cells[$cols[$i]][0].Trim()}else{''}"
    "   })"
    "   $content=@($vals|Where-Object {$_ -ne '' -and $_ -ne '__AUTO__'})"
    "   if($content.Count -eq 0){continue}"
    "   if($skip){$skip=$false;continue}"
    "   foreach($col in $cols){if($cells.ContainsKey($col)){Need (-not $cells[$col][1]) ('Formula cell rejected in row '+$rn.GetAttribute('r')+'. Paste values first.')}}"
    "   $serial++;Need ($serial -le 20000) 'Maximum 20000 Excel rows.'"
    "   try{$rows.Add((ExcelRow $vals $serial))}catch{throw ('XLSX row '+$rn.GetAttribute('r')+': '+$_.Exception.Message)}"
    "  }"
    "  CheckRows $rows;return $rows.ToArray()"
    " }finally{$zip.Dispose()}"
    "}"
    ""
    "$script:Notice=''"
    "$stage=$null"
    "try{"
    " if($Mode -eq 'SelfTest'){return}"
    " Need ([IO.File]::Exists($Source)) 'Input file not found.'"
    " Need (-not [IO.File]::Exists($Target)) 'Output file exists; overwrite disabled.'"
    " Need ((Get-Item -LiteralPath $Source).Length -le 33554432) 'Input exceeds 32 MB limit.'"
    " $stage=[IO.Path]::Combine([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Target)),('bplus-'+[Guid]::NewGuid().ToString('N')+[IO.Path]::GetExtension($Target)))"
    " if($Mode -eq 'SheetList'){"
    "  $rows=@(ExcelSheets $Source $Format)"
    "  $listing=@(for($i=0;$i -lt $rows.Count;$i++){([string]($i+1))+': '+[regex]::Replace($rows[$i],'[^\\x20-\\x7E]','?')})"
    "  [IO.File]::WriteAllLines($stage,[string[]]$listing,$Ascii)"
    " }elseif($Mode -eq 'Read'){"
    "  $rows=@(switch($Format){'GSI'{ReadGsi $Source};'SDR'{ReadSdr $Source};'KML'{ReadKml $Source};'KMZ'{ReadKml $Source};'XLSX'{ReadXlsx $Source};default{throw 'Unsupported input format.'}})"
    "  WriteBridge $rows $stage"
    " }else{"
    "  $rows=@(ReadBridge $Source)"
    "  switch($Format){'GSI8'{WriteGsi $rows $stage 8};'GSI16'{WriteGsi $rows $stage 16};'SDR'{WriteSdr $rows $stage};'KML'{WriteKml $rows $stage};'KMZ'{WriteKml $rows $stage};default{throw 'Unsupported output format.'}}"
    " }"
    " [IO.File]::Move($stage,$Target);$stage=$null"
    " [IO.File]::WriteAllText($Status,('OK '+$rows.Count+$(if($Mode -eq 'SheetList'){' worksheets. '}else{' points. '})+$script:Notice),$Ascii)"
    "}catch{"
    " if($stage -and [IO.File]::Exists($stage)){[IO.File]::Delete($stage)}"
    " [IO.File]::WriteAllText($Status,('ERROR: '+$_.Exception.Message),$Ascii)"
    " exit 1"
    "}"
  ))

(princ "\nBPlus CAD Tools 1.1 TRIAL loaded. Top menu is added automatically; fallback BPPANEL or BP.")
(bp:showmenu)
(princ)
