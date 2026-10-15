;;; ============================================================================
;;; FrameBlock.lsp
;;;
;;; Places the company frame block at a user-selected scale, centred on a
;;; polyline that is either picked from the drawing or drawn on the spot.
;;;
;;; Commands:
;;;   FRAMEBLOCK  (alias FB)
;;;
;;; How it works:
;;;   1. Choose a scale (1:50, 50, 1:2.5 ... or press Enter for the last one).
;;;   2. Select an existing polyline, or choose "Draw" to draw one.
;;;   3. The frame block is inserted at that scale, centred on the polyline's
;;;      extents. If the polyline will not fit inside the frame you are warned.
;;;   4. If the block has an attribute tagged SCALE (see *FB:ScaleTag*), it is
;;;      filled in with the scale text, e.g. "1:50".
;;;
;;; Setup - edit the values in the CONFIGURATION section below:
;;;   *FB:BlockName*  name of the frame block in your drawings / template
;;;   *FB:BlockFile*  full path to the block .dwg, used when the block is not
;;;                   already defined in the current drawing (nil = search the
;;;                   AutoCAD support paths for <BlockName>.dwg)
;;;   *FB:Layer*      layer to put the frame on (nil = current layer)
;;;
;;; The frame block must be drawn at 1:1 (paper size), so a scale of 1:50
;;; inserts it 50x larger.
;;; ============================================================================

(vl-load-com)

;;; ---------------------------- CONFIGURATION ---------------------------------
(setq *FB:BlockName* "COMPANY_FRAME")   ; block name
(setq *FB:BlockFile* nil)               ; e.g. "C:\\CAD\\Blocks\\COMPANY_FRAME.dwg"
(setq *FB:Layer*     nil)               ; e.g. "FRAME"
(setq *FB:ScaleTag*  "SCALE")           ; attribute tag to receive "1:xx"
(if (not *FB:LastScale*) (setq *FB:LastScale* 1.0))
;;; ----------------------------------------------------------------------------

;; Format a number without trailing zeros: 50.0 -> "50", 2.5 -> "2.5"
(defun FB:num->str (n / s)
  (setq s (rtos n 2 4))
  (if (vl-string-search "." s)
    (setq s (vl-string-right-trim "." (vl-string-right-trim "0" s)))
  )
  s
)

;; Parse "1:50", "50", "2:1" etc. into a scale factor. Returns nil if invalid.
(defun FB:parse-scale (str / pos a b)
  (setq str (vl-string-trim " " str))
  (cond
    ((setq pos (vl-string-search ":" str))
     (setq a (distof (substr str 1 pos))
           b (distof (substr str (+ pos 2)))
     )
     (if (and a b (> a 0) (> b 0)) (/ b a))
    )
    ((and (setq a (distof str)) (> a 0)) a)
  )
)

;; Scale factor -> "1:50" (or "2:1" for enlargements)
(defun FB:scale->text (sc)
  (if (>= sc 1.0)
    (strcat "1:" (FB:num->str sc))
    (strcat (FB:num->str (/ 1.0 sc)) ":1")
  )
)

;; Ask the user for a scale, defaulting to the last one used.
(defun FB:get-scale (/ str sc)
  (while (not sc)
    (setq str (getstring
                (strcat "\nEnter frame scale (e.g. 1:50) <"
                        (FB:scale->text *FB:LastScale*) ">: "
                )
              )
    )
    (if (= str "")
      (setq sc *FB:LastScale*)
      (if (not (setq sc (FB:parse-scale str)))
        (princ "\nInvalid scale. Use a form like 1:50, 50 or 2:1.")
      )
    )
  )
  (setq *FB:LastScale* sc)
)

;; Returns (minpt maxpt) of a VLA object's bounding box in WCS.
(defun FB:extents (obj / mn mx)
  (vla-getboundingbox obj 'mn 'mx)
  (list (vlax-safearray->list mn) (vlax-safearray->list mx))
)

(defun FB:midpoint (ext)
  (mapcar '(lambda (a b) (/ (+ a b) 2.0)) (car ext) (cadr ext))
)

(defun FB:is-polyline (ent)
  (member (cdr (assoc 0 (entget ent))) '("LWPOLYLINE" "POLYLINE"))
)

;; Let the user pick a polyline or draw a new one. Returns the entity or nil.
(defun FB:get-polyline (/ ent sel last)
  (while (not ent)
    (setvar "ERRNO" 0)
    (initget "Draw")
    (setq sel (entsel "\nSelect polyline for frame or [Draw] <Draw>: "))
    (cond
      ((or (null sel) (= sel "Draw"))
       (if (and (null sel) (= (getvar "ERRNO") 52)) (setq sel "Draw"))
       (if (= sel "Draw")
         (progn
           (setq last (entlast))
           (princ "\nDraw the polyline (Enter to finish, C to close):")
           (setvar "CMDECHO" 1)
           (command "_.PLINE")
           (while (> (logand (getvar "CMDACTIVE") 1) 0) (command pause))
           (setvar "CMDECHO" 0)
           (if (and (entlast)
                    (not (equal (entlast) last))
                    (FB:is-polyline (entlast))
               )
             (setq ent (entlast))
             (progn (princ "\nNo polyline was drawn.") (setq ent 'cancel))
           )
         )
         (princ "\nMissed, try again.")
       )
      )
      ((FB:is-polyline (car sel)) (setq ent (car sel)))
      (t (princ "\nThat is not a polyline."))
    )
  )
  (if (/= ent 'cancel) ent)
)

;; Block reference (name or file path) to pass to InsertBlock, or nil.
(defun FB:block-source (doc / path)
  (cond
    ((tblsearch "BLOCK" *FB:BlockName*) *FB:BlockName*)
    ((and *FB:BlockFile* (findfile *FB:BlockFile*)) (findfile *FB:BlockFile*))
    ((setq path (findfile (strcat *FB:BlockName* ".dwg"))) path)
  )
)

;; Model space or paper space, whichever the user is currently working in.
(defun FB:active-space (doc)
  (if (and (= (getvar "TILEMODE") 0) (= (getvar "CVPORT") 1))
    (vla-get-paperspace doc)
    (vla-get-modelspace doc)
  )
)

(defun FB:set-scale-attrib (blk txt)
  (if (and *FB:ScaleTag* (= (vla-get-hasattributes blk) :vlax-true))
    (foreach att (vlax-invoke blk 'getattributes)
      (if (= (strcase (vla-get-tagstring att)) (strcase *FB:ScaleTag*))
        (vla-put-textstring att txt)
      )
    )
  )
)

(defun c:FRAMEBLOCK (/ *error* doc space src sc pl plExt plCen
                       tmp blkExt blkCen blkSize plSize ins blk oldCmd)
  (setq doc (vla-get-activedocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (if (and tmp (not (vlax-erased-p tmp))) (vla-delete tmp))
    (if oldCmd (setvar "CMDECHO" oldCmd))
    (vla-endundomark doc)
    (if (not (wcmatch (strcase msg t) "*break*,*cancel*,*exit*"))
      (princ (strcat "\nError: " msg))
    )
    (princ)
  )

  (setq oldCmd (getvar "CMDECHO"))
  (setvar "CMDECHO" 0)
  (vla-startundomark doc)

  (cond
    ((not (setq src (FB:block-source doc)))
     (princ (strcat "\nFrame block \"" *FB:BlockName*
                    "\" not found in the drawing or support paths."
                    "\nSet *FB:BlockFile* at the top of FrameBlock.lsp."
            )
     )
    )
    ((not (setq sc (FB:get-scale))))
    ((not (setq pl (FB:get-polyline))))
    (t
     (setq space (FB:active-space doc)
           plExt (FB:extents (vlax-ename->vla-object pl))
           plCen (FB:midpoint plExt)
     )
     ;; Insert a temporary copy at the origin to measure the frame.
     (setq tmp    (vla-insertblock space (vlax-3d-point '(0 0 0)) src sc sc sc 0)
           blkExt (FB:extents tmp)
           blkCen (FB:midpoint blkExt)
     )
     (vla-delete tmp)
     (setq tmp nil)

     ;; Shift the insertion point so the frame's centre lands on the
     ;; polyline's centre.
     (setq ins (mapcar '- plCen blkCen))
     (setq ins (list (car ins) (cadr ins) 0.0))
     (setq blk (vla-insertblock space (vlax-3d-point ins)
                 (if (tblsearch "BLOCK" *FB:BlockName*) *FB:BlockName* src)
                 sc sc sc 0
               )
     )
     (if (and *FB:Layer* (tblsearch "LAYER" *FB:Layer*))
       (vla-put-layer blk *FB:Layer*)
     )
     (FB:set-scale-attrib blk (FB:scale->text sc))

     ;; Warn if the polyline is larger than the frame.
     (setq blkSize (mapcar '- (cadr blkExt) (car blkExt))
           plSize  (mapcar '- (cadr plExt) (car plExt))
     )
     (if (or (> (car plSize) (car blkSize)) (> (cadr plSize) (cadr blkSize)))
       (princ "\nWarning: the polyline is larger than the frame at this scale.")
     )
     (princ (strcat "\nFrame placed at " (FB:scale->text sc) "."))
    )
  )

  (setvar "CMDECHO" oldCmd)
  (vla-endundomark doc)
  (princ)
)

(defun c:FB () (c:FRAMEBLOCK))

(princ "\nFrameBlock loaded. Type FRAMEBLOCK or FB to run.")
(princ)
